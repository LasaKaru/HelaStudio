using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using StackExchange.Redis;

namespace Shellwright.Orchestrator.Logs;

/// <summary>Log pipeline settings.</summary>
public sealed class LogPipelineOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "BuildLogs";

    /// <summary>Where the durable archive is written.</summary>
    [Required]
    public string ArchiveRoot { get; set; } = "/var/lib/shellwright/build-logs";

    /// <summary>
    /// How many lines the live stream keeps.
    /// </summary>
    /// <remarks>
    /// ⚠️ Bounded, and the bound is the point. A verbose Gradle build produces
    /// tens of megabytes; an unbounded stream would put all of it in Redis,
    /// which on the free tier is the whole memory allowance and on our own host
    /// is memory Postgres needed. Fifty thousand lines is far more than anyone
    /// scrolls and a small fraction of a large build.
    /// </remarks>
    [Range(1000, 1_000_000)]
    public int LiveStreamLines { get; set; } = 50_000;

    /// <summary>
    /// How many lines are batched before writing to Redis.
    /// </summary>
    /// <remarks>
    /// ⚠️ Batched because the free Redis tiers meter commands, not bytes. A
    /// build that emits 200,000 lines is 200,000 commands unbatched, which is
    /// twenty times a day's allowance on Upstash's free plan for a single
    /// build. At fifty lines a batch it is four thousand.
    /// </remarks>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Where Redis is, or empty to archive only.
    /// </summary>
    /// <remarks>
    /// ⚠️ Optional on purpose. A worker with no Redis configured still runs
    /// builds and still keeps their logs; what it loses is the ability to watch
    /// one happen. That is the right failure for a self-hosted deployment that
    /// has not stood a Redis up yet, and it is the same degradation a Redis
    /// outage produces at run time.
    /// </remarks>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>How long a batch may wait before being flushed anyway.</summary>
    /// <remarks>
    /// Without this a build that goes quiet — a long compile — would hold its
    /// last partial batch, and the person watching would see the log stop at
    /// exactly the moment they most want to know it is still alive.
    /// </remarks>
    public TimeSpan MaxBatchDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>
/// Streams build output to Redis for live viewing and to disk for keeping.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Two destinations, and they fail independently on purpose. The live stream
/// is a convenience: if Redis is down, or nobody is watching, or a viewer is
/// too slow, the build carries on and the archive is complete. The archive is
/// the record, and a failure there is a real failure.
/// </para>
/// <para>
/// ⚠️ Nothing is buffered whole. A build's log is tens of megabytes and there
/// may be several running; holding one in memory to write it at the end is how
/// the orchestrator gets killed by the OOM killer during the largest build.
/// </para>
/// </remarks>
/// <param name="redis">Connection to Redis, or null when the live stream is disabled.</param>
/// <param name="options">Pipeline settings.</param>
/// <param name="logger">Where pipeline problems are reported.</param>
public sealed class RedisLogPipeline(
    IConnectionMultiplexer? redis,
    IOptions<LogPipelineOptions> options,
    ILogger<RedisLogPipeline> logger) : IBuildLogPipeline, IAsyncDisposable
{
    private readonly LogPipelineOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly Dictionary<Guid, BuildLogWriter> writers = [];
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>The Redis stream key a build's live log is published on.</summary>
    /// <param name="buildId">The build.</param>
    /// <returns>The key.</returns>
    public static string StreamKey(Guid buildId) =>
        string.Create(CultureInfo.InvariantCulture, $"build:{buildId}:logs");

    /// <inheritdoc />
    public async Task AppendAsync(
        Guid buildId,
        string line,
        bool isError,
        CancellationToken cancellationToken = default)
    {
        var writer = await WriterAsync(buildId, cancellationToken);

        // ⚠️ Redacted here, on the way in. Redacting at render time would leave
        // the secret in the stream and in the archive, and the archive is the
        // copy that lives for years.
        await writer.AppendAsync(LogRedaction.Process(line, isError), cancellationToken);
    }

    /// <inheritdoc />
    public async Task ArchiveAsync(Guid buildId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (writers.Remove(buildId, out var writer))
            {
                await writer.DisposeAsync();
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
        foreach (var writer in writers.Values)
        {
            await writer.DisposeAsync();
        }

        writers.Clear();
        gate.Dispose();
    }

    private async Task<BuildLogWriter> WriterAsync(Guid buildId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (!writers.TryGetValue(buildId, out var writer))
            {
                writer = new BuildLogWriter(buildId, redis, settings, logger);
                writers[buildId] = writer;
            }

            return writer;
        }
        finally
        {
            gate.Release();
        }
    }
}
