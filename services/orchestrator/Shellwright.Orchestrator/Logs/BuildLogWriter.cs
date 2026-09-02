using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shellwright.Orchestrator.Logs;

/// <summary>
/// One build's log, on its way to two places at once.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The archive is written first and the live stream second, and a failure of
/// the second is swallowed. That ordering is the whole design: the archive is
/// the record a customer can come back to in six months, and the live stream is
/// a convenience for whoever happens to be watching. Losing the convenience
/// must never cost the record, and a build must never fail because Redis is
/// having a bad afternoon.
/// </para>
/// <para>
/// Lines are batched before reaching Redis because the free tiers meter
/// commands rather than bytes, and a verbose build is hundreds of thousands of
/// lines. The archive takes every line as it arrives and flushes it, so the
/// record survives a worker that dies mid-build.
/// </para>
/// </remarks>
public sealed class BuildLogWriter : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Guid buildId;
    private readonly IConnectionMultiplexer? redis;
    private readonly LogPipelineOptions settings;
    private readonly ILogger logger;
    private readonly StreamWriter archive;
    private readonly List<LogLine> pending;
    private readonly SemaphoreSlim gate = new(1, 1);

    private DateTimeOffset lastFlush = DateTimeOffset.UtcNow;
    private bool liveStreamGaveUp;
    private long droppedFromLiveStream;

    /// <summary>Opens the archive and prepares the live stream.</summary>
    /// <param name="buildId">Which build.</param>
    /// <param name="redis">Connection to Redis, or null to archive only.</param>
    /// <param name="settings">Pipeline settings.</param>
    /// <param name="logger">Where pipeline problems are reported.</param>
    public BuildLogWriter(
        Guid buildId,
        IConnectionMultiplexer? redis,
        LogPipelineOptions settings,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.buildId = buildId;
        this.redis = redis;
        this.settings = settings;
        this.logger = logger;

        pending = new List<LogLine>(settings.BatchSize);

        Directory.CreateDirectory(settings.ArchiveRoot);
        ArchivePath = Path.Combine(settings.ArchiveRoot, $"{buildId:N}.ndjson");

        // Append, not create. A worker that restarts mid-build resumes into the
        // same file rather than truncating what it already wrote.
        archive = new StreamWriter(
            new FileStream(ArchivePath, FileMode.Append, FileAccess.Write, FileShare.Read),
            leaveOpen: false)
        {
            // ⚠️ Flushed per line, and the cost is accepted deliberately. A
            // StreamWriter left to its own buffering holds the last kilobyte or
            // so of output in managed memory, and the moment that costs us is a
            // crash — where the lines still in the buffer are exactly the lines
            // that say why. It also means a live tail of the archive shows the
            // build stopping several lines before it did.
            //
            // This is a write(2) per line, not an fsync: the kernel still gets
            // to batch the actual disk writes, so a verbose build pays syscalls
            // rather than seeks. Losing the tail of a failed build's log is the
            // more expensive of the two.
            AutoFlush = true,
        };
    }

    /// <summary>Where the durable record is written.</summary>
    public string ArchivePath { get; }

    /// <summary>How many lines never reached the live stream.</summary>
    /// <remarks>
    /// Surfaced so the UI can say "you fell behind" rather than silently
    /// showing an incomplete log as though it were complete.
    /// </remarks>
    public long DroppedFromLiveStream => Interlocked.Read(ref droppedFromLiveStream);

    /// <summary>Writes one line to both destinations.</summary>
    /// <param name="line">The line, already redacted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the archive has it.</returns>
    public async Task AppendAsync(LogLine line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);

        await gate.WaitAsync(cancellationToken);

        try
        {
            await archive.WriteLineAsync(JsonSerializer.Serialize(line, JsonOptions));

            pending.Add(line);

            var due = pending.Count >= settings.BatchSize
                || DateTimeOffset.UtcNow - lastFlush >= settings.MaxBatchDelay;

            if (due)
            {
                await FlushLiveAsync();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();

        try
        {
            await FlushLiveAsync();
            await archive.FlushAsync();
            await archive.DisposeAsync();
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private async Task FlushLiveAsync()
    {
        if (pending.Count == 0)
        {
            return;
        }

        lastFlush = DateTimeOffset.UtcNow;

        if (redis is null || liveStreamGaveUp)
        {
            Interlocked.Add(ref droppedFromLiveStream, pending.Count);
            pending.Clear();
            return;
        }

        try
        {
            var database = redis.GetDatabase();
            var key = RedisLogPipeline.StreamKey(buildId);

            var batch = database.CreateBatch();
            var writes = new List<Task>(pending.Count);

            foreach (var line in pending)
            {
                writes.Add(batch.StreamAddAsync(
                    key,
                    [
                        new NameValueEntry("text", line.Text),
                        new NameValueEntry("severity", line.Severity.ToString()),
                        new NameValueEntry(
                            "redacted",
                            line.Redacted.ToString(CultureInfo.InvariantCulture)),
                    ],
                    messageId: null,

                    // ⚠️ Approximate trimming. Exact trimming makes Redis scan
                    // to find the precise boundary on every add, which on a
                    // stream this hot is the difference between a bounded cost
                    // and a growing one. The bound is a memory guard, not an
                    // accounting figure.
                    maxLength: settings.LiveStreamLines,
                    useApproximateMaxLength: true));
            }

            batch.Execute();
            await Task.WhenAll(writes);
        }
        catch (RedisException exception)
        {
            // ⚠️ Swallowed, once, and then the live stream is abandoned for
            // this build. A build must not fail because nobody could watch it,
            // and retrying a failing Redis on every batch turns one outage into
            // a slow build for everyone.
            liveStreamGaveUp = true;
            Interlocked.Add(ref droppedFromLiveStream, pending.Count);

            logger.LogWarning(
                exception,
                "Live log streaming for build {BuildId} has been abandoned. The archive at {ArchivePath} is unaffected.",
                buildId,
                ArchivePath);
        }
        finally
        {
            pending.Clear();
        }
    }
}
