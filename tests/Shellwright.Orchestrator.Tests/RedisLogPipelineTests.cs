using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Logs;
using Shellwright.Orchestrator.Tests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-029–034 — what the log pipeline promises, against a real Redis
/// and a real disk.
/// </summary>
/// <remarks>
/// ⚠️ The promises worth testing here are the awkward ones: that the archive
/// survives the live stream failing, that the live stream is bounded, that a
/// viewer can reconnect without rewinding, and that a secret in build output
/// reaches neither destination. The happy path — a line goes in, a line comes
/// out — is the part that was never going to break.
/// </remarks>
/// <param name="redis">The shared Redis.</param>
[Collection(RedisFixtureDefinition.Name)]
public sealed class RedisLogPipelineTests(RedisFixture redis) : IDisposable
{
    private readonly string archiveRoot = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-logs-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(archiveRoot))
        {
            Directory.Delete(archiveRoot, recursive: true);
        }
    }

    [Fact(DisplayName = "Every appended line reaches the live stream once the batch flushes")]
    public async Task LinesReachTheLiveStream()
    {
        var buildId = Guid.NewGuid();
        var options = Options(batchSize: 10);

        await using (var pipeline = Pipeline(options))
        {
            for (var index = 0; index < 25; index++)
            {
                await pipeline.AppendAsync(buildId, $"line {index}", isError: false);
            }

            await pipeline.ArchiveAsync(buildId);
        }

        var page = await new BuildLogReader(redis.Connection)
            .ReadAsync(buildId, BuildLogReader.Beginning, count: 100);

        Assert.Equal(25, page.Lines.Length);
        Assert.Equal("line 0", page.Lines[0].Line.Text);
        Assert.Equal("line 24", page.Lines[^1].Line.Text);
    }

    [Fact(DisplayName = "A partial batch is held back until the writer is closed")]
    public async Task PartialBatchesAreBatched()
    {
        var buildId = Guid.NewGuid();

        // A batch of fifty and a delay long enough that it cannot be the delay
        // that flushes: what is being asserted is that batching is real, not
        // that a timer eventually fires.
        var options = Options(batchSize: 50, maxBatchDelay: TimeSpan.FromHours(1));

        await using var pipeline = Pipeline(options);

        for (var index = 0; index < 5; index++)
        {
            await pipeline.AppendAsync(buildId, $"line {index}", isError: false);
        }

        var reader = new BuildLogReader(redis.Connection);
        var beforeClose = await reader.ReadAsync(buildId, BuildLogReader.Beginning);

        Assert.Empty(beforeClose.Lines);

        // ...and the archive already has all five, because the archive is never
        // the thing that waits.
        var archived = await ArchivedLinesAsync(options, buildId);
        Assert.Equal(5, archived.Count);

        await pipeline.ArchiveAsync(buildId);

        var afterClose = await reader.ReadAsync(buildId, BuildLogReader.Beginning);
        Assert.Equal(5, afterClose.Lines.Length);
    }

    [Fact(DisplayName = "The archive is complete when there is no live stream at all")]
    public async Task ArchiveSurvivesWithoutRedis()
    {
        var buildId = Guid.NewGuid();
        var options = Options(batchSize: 4);

        var writer = new BuildLogWriter(buildId, redis: null, options.Value, NullLogger.Instance);

        await using (writer)
        {
            for (var index = 0; index < 10; index++)
            {
                await writer.AppendAsync(new LogLine($"line {index}", LogSeverity.Info, Redacted: false));
            }
        }

        var archived = await ArchivedLinesAsync(options, buildId);

        Assert.Equal(10, archived.Count);
        Assert.Equal("line 9", archived[^1].Text);

        // And it says so, rather than presenting a hole as a complete log.
        Assert.Equal(10, writer.DroppedFromLiveStream);
    }

    [Fact(DisplayName = "A live stream that fails mid-build is abandoned, not retried, and the archive is untouched")]
    public async Task LiveStreamFailureIsAbandoned()
    {
        var buildId = Guid.NewGuid();
        var options = Options(batchSize: 2);

        // ⚠️ The failure is made real rather than mocked: the stream key is
        // occupied by a string, so Redis answers WRONGTYPE to every XADD. That
        // is a RedisException raised by Redis itself, which is the case the
        // swallow was written for.
        await redis.Connection.GetDatabase()
            .StringSetAsync(RedisLogPipeline.StreamKey(buildId), "not a stream");

        var writer = new BuildLogWriter(buildId, redis.Connection, options.Value, NullLogger.Instance);

        await using (writer)
        {
            for (var index = 0; index < 8; index++)
            {
                await writer.AppendAsync(new LogLine($"line {index}", LogSeverity.Info, Redacted: false));
            }
        }

        var archived = await ArchivedLinesAsync(options, buildId);

        Assert.Equal(8, archived.Count);
        Assert.Equal(8, writer.DroppedFromLiveStream);
    }

    [Fact(DisplayName = "The live stream stays bounded when a build is far more verbose than the bound")]
    public async Task LiveStreamIsBounded()
    {
        var buildId = Guid.NewGuid();

        // A bound far below the default, so the test is a few thousand lines
        // rather than a few hundred thousand. Approximate trimming means Redis
        // may keep more than the bound, but it must not keep everything.
        var options = Options(batchSize: 100, liveStreamLines: 1_000);

        await using (var pipeline = Pipeline(options))
        {
            for (var index = 0; index < 20_000; index++)
            {
                await pipeline.AppendAsync(buildId, $"line {index}", isError: false);
            }

            await pipeline.ArchiveAsync(buildId);
        }

        var length = await redis.Connection.GetDatabase()
            .StreamLengthAsync(RedisLogPipeline.StreamKey(buildId));

        Assert.True(
            length < 20_000,
            $"The stream kept all {length} lines; the bound of 1,000 did nothing.");

        // The archive, meanwhile, kept every one of them.
        var archived = await ArchivedLinesAsync(options, buildId);
        Assert.Equal(20_000, archived.Count);
    }

    [Fact(DisplayName = "A viewer resumes from its last id without rewinding or skipping")]
    public async Task ResumesFromLastStreamId()
    {
        var buildId = Guid.NewGuid();
        var options = Options(batchSize: 5);
        var reader = new BuildLogReader(redis.Connection);

        await using var pipeline = Pipeline(options);

        for (var index = 0; index < 10; index++)
        {
            await pipeline.AppendAsync(buildId, $"line {index}", isError: false);
        }

        // A viewer that asks for less than there is gets a page, not the lot.
        var first = await reader.ReadAsync(buildId, BuildLogReader.Beginning, count: 6);
        Assert.Equal(6, first.Lines.Length);
        Assert.Equal("line 0", first.Lines[0].Line.Text);
        Assert.Equal("line 5", first.Lines[^1].Line.Text);

        // Resuming from its last id continues at the next line — it neither
        // repeats line 5 nor skips line 6.
        var rest = await reader.ReadAsync(buildId, first.LastStreamId, count: 100);
        Assert.Equal(4, rest.Lines.Length);
        Assert.Equal("line 6", rest.Lines[0].Line.Text);
        Assert.Equal("line 9", rest.Lines[^1].Line.Text);

        // Caught up, and the build has gone quiet: an empty page must hand back
        // the same position rather than rewinding the viewer to the beginning.
        var idle = await reader.ReadAsync(buildId, rest.LastStreamId, count: 100);
        Assert.Empty(idle.Lines);
        Assert.Equal(rest.LastStreamId, idle.LastStreamId);

        // And a line written afterwards arrives on the next poll.
        await pipeline.AppendAsync(buildId, "line 10", isError: false);
        await pipeline.ArchiveAsync(buildId);

        var resumed = await reader.ReadAsync(buildId, idle.LastStreamId, count: 100);
        Assert.Equal("line 10", Assert.Single(resumed.Lines).Line.Text);
    }

    [Fact(DisplayName = "Severity and the redaction flag survive the round trip")]
    public async Task FieldsSurviveTheRoundTrip()
    {
        var buildId = Guid.NewGuid();

        await using (var pipeline = Pipeline(Options(batchSize: 1)))
        {
            await pipeline.AppendAsync(buildId, "> Task :app:compileDebugKotlin", isError: false);

            // Standard output, and still an error: severity is decided by the
            // text, because Gradle writes compilation failures to stdout.
            await pipeline.AppendAsync(
                buildId,
                "e: Main.kt:12:5 error: unresolved reference: foo",
                isError: false);
        }

        var page = await new BuildLogReader(redis.Connection)
            .ReadAsync(buildId, BuildLogReader.Beginning);

        Assert.Equal(2, page.Lines.Length);
        Assert.Equal(LogSeverity.Info, page.Lines[0].Line.Severity);
        Assert.Equal(LogSeverity.Error, page.Lines[1].Line.Severity);
        Assert.All(page.Lines, line => Assert.False(line.Line.Redacted));
    }

    [Fact(DisplayName = "A secret in build output reaches neither the stream nor the archive")]
    public async Task SecretsReachNeitherDestination()
    {
        var buildId = Guid.NewGuid();
        var options = Options(batchSize: 1);

        // ⚠️ Taken from the shared corpus rather than written here. A test that
        // invents its own secret shape proves the pipeline redacts that shape;
        // this proves it redacts the shapes the redactor is actually held to,
        // and a case added to the corpus is covered here for free.
        var leak = RedactionCorpus.Case("a google api key in a merged manifest");
        var secret = Assert.Single(leak.MustNotContain);

        await using (var pipeline = Pipeline(options))
        {
            await pipeline.AppendAsync(buildId, leak.Line, isError: false);
        }

        var page = await new BuildLogReader(redis.Connection)
            .ReadAsync(buildId, BuildLogReader.Beginning);

        var streamed = Assert.Single(page.Lines);
        Assert.DoesNotContain(secret, streamed.Line.Text, StringComparison.Ordinal);
        Assert.True(streamed.Line.Redacted);

        var archived = Assert.Single(await ArchivedLinesAsync(options, buildId));
        Assert.DoesNotContain(secret, archived.Text, StringComparison.Ordinal);
        Assert.True(archived.Redacted);

        // And not anywhere else in the file either — not in a field name, not
        // in a line the redactor let through for a different reason.
        var raw = await File.ReadAllTextAsync(ArchivePath(options, buildId));
        Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "A worker that restarts mid-build appends to the archive rather than truncating it")]
    public async Task ArchiveIsAppendedOnRestart()
    {
        var buildId = Guid.NewGuid();
        var options = Options(batchSize: 1);

        await using (var first = Pipeline(options))
        {
            await first.AppendAsync(buildId, "before the restart", isError: false);
            await first.ArchiveAsync(buildId);
        }

        await using (var second = Pipeline(options))
        {
            await second.AppendAsync(buildId, "after the restart", isError: false);
            await second.ArchiveAsync(buildId);
        }

        var archived = await ArchivedLinesAsync(options, buildId);

        Assert.Equal(2, archived.Count);
        Assert.Equal("before the restart", archived[0].Text);
        Assert.Equal("after the restart", archived[1].Text);
    }

    [Fact(DisplayName = "Concurrent appends to several builds keep each archive to its own lines")]
    public async Task BuildsDoNotBleedIntoEachOther()
    {
        var options = Options(batchSize: 8);
        var builds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();

        await using (var pipeline = Pipeline(options))
        {
            await Task.WhenAll(builds.Select(async (buildId, position) =>
            {
                for (var index = 0; index < 200; index++)
                {
                    await pipeline.AppendAsync(buildId, $"build {position} line {index}", isError: false);
                }
            }));

            foreach (var buildId in builds)
            {
                await pipeline.ArchiveAsync(buildId);
            }
        }

        for (var position = 0; position < builds.Length; position++)
        {
            var archived = await ArchivedLinesAsync(options, builds[position]);

            Assert.Equal(200, archived.Count);
            Assert.All(
                archived,
                line => Assert.StartsWith($"build {position} ", line.Text, StringComparison.Ordinal));
        }
    }

    private static string ArchivePath(IOptions<LogPipelineOptions> options, Guid buildId) =>
        Path.Combine(options.Value.ArchiveRoot, $"{buildId:N}.ndjson");

    private static async Task<IReadOnlyList<LogLine>> ArchivedLinesAsync(
        IOptions<LogPipelineOptions> options,
        Guid buildId)
    {
        var lines = new List<LogLine>();

        // Shared read: the writer may still hold the file open, which is
        // exactly the state a live tail of the archive would find it in.
        await using var file = new FileStream(
            ArchivePath(options, buildId),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        using var reader = new StreamReader(file);

        while (await reader.ReadLineAsync() is { } text)
        {
            lines.Add(JsonSerializer.Deserialize<LogLine>(text, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException($"Archive line did not deserialize: {text}"));
        }

        return lines;
    }

    private IOptions<LogPipelineOptions> Options(
        int batchSize,
        int liveStreamLines = 50_000,
        TimeSpan? maxBatchDelay = null) =>
        Microsoft.Extensions.Options.Options.Create(new LogPipelineOptions
        {
            ArchiveRoot = archiveRoot,
            BatchSize = batchSize,
            LiveStreamLines = liveStreamLines,
            MaxBatchDelay = maxBatchDelay ?? TimeSpan.FromHours(1),
        });

    private RedisLogPipeline Pipeline(IOptions<LogPipelineOptions> options) =>
        new(redis.Connection, options, NullLogger<RedisLogPipeline>.Instance);
}
