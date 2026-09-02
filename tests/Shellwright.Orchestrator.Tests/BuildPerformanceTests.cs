using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Logs;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Tests.Infrastructure;
using Shellwright.Orchestrator.Workflows;
using Xunit;
using Xunit.Abstractions;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-PERF-001–004 — what the hot paths actually cost.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Budgets, not benchmarks. Each of these asserts a ceiling with several
/// times the measured headroom, so it is a regression alarm rather than a
/// flaky micro-measurement — a number recorded once in a document is a number
/// nobody notices going bad.
/// </para>
/// <para>
/// ⚠️ Measured on this container, where nothing crosses a network. These are
/// floors: they say the code is not the bottleneck. They say nothing about the
/// Oracle Always Free host, which contends two cores between the API,
/// PostgreSQL, Redis and Temporal.
/// </para>
/// </remarks>
/// <param name="redis">The shared Redis.</param>
/// <param name="output">Where measurements are written.</param>
[Collection(RedisFixtureDefinition.Name)]
public sealed class BuildPerformanceTests(RedisFixture redis, ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-perf-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Redaction costs a fraction of a millisecond per line")]
    public void RedactionIsCheapPerLine()
    {
        // ⚠️ On the hot path of every line of every build. A verbose Gradle
        // build emits a few hundred thousand lines, so a millisecond each is
        // several minutes of runner time spent on regular expressions — which
        // would be charged to a customer as build time.
        var corpus = RedactionCorpus.Cases.Select(x => x.Line).ToArray();
        const int Iterations = 20_000;

        // Warm the regular expressions, so the figure is steady-state rather
        // than including one-time compilation.
        foreach (var line in corpus)
        {
            LogRedaction.Process(line, isError: false);
        }

        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < Iterations; index++)
        {
            LogRedaction.Process(corpus[index % corpus.Length], isError: false);
        }

        stopwatch.Stop();

        var perLine = stopwatch.Elapsed.TotalMilliseconds / Iterations;
        Report("redaction", $"{perLine:F4} ms/line over {Iterations:N0} lines");

        perLine.Should().BeLessThan(
            0.5,
            "redaction runs on every line of every build; at half a millisecond a 200,000-line "
            + "build would spend a minute and a half of billable runner time in regular expressions");
    }

    [Fact(DisplayName = "The log pipeline sustains a verbose build's output")]
    public async Task LogPipelineKeepsUp()
    {
        var buildId = Guid.NewGuid();
        var options = Options.Create(new LogPipelineOptions
        {
            ArchiveRoot = Path.Combine(root, "logs"),
            BatchSize = 50,
            LiveStreamLines = 50_000,
            MaxBatchDelay = TimeSpan.FromMilliseconds(500),
        });

        const int Lines = 20_000;

        var stopwatch = Stopwatch.StartNew();

        await using (var pipeline = new RedisLogPipeline(
            redis.Connection,
            options,
            NullLogger<RedisLogPipeline>.Instance))
        {
            for (var index = 0; index < Lines; index++)
            {
                await pipeline.AppendAsync(
                    buildId,
                    $"> Task :app:compileDebugKotlin line {index} of a fairly ordinary build",
                    isError: false);
            }

            await pipeline.ArchiveAsync(buildId);
        }

        stopwatch.Stop();

        var perSecond = Lines / stopwatch.Elapsed.TotalSeconds;
        Report("log pipeline", $"{perSecond:N0} lines/s for {Lines:N0} lines (redact, archive, stream)");

        // ⚠️ Gradle at its most verbose emits a few thousand lines a second.
        // Falling below that means the pipeline becomes the build's bottleneck
        // and the customer pays for the orchestrator's own I/O.
        perSecond.Should().BeGreaterThan(
            2_000,
            "the pipeline must not become the bottleneck a verbose build waits on");
    }

    [Fact(DisplayName = "Patching a realistic APK takes seconds, not minutes")]
    public async Task PatchingIsFast()
    {
        var request = new BuildRequest(
            BuildId: Guid.NewGuid(),
            OrgId: Guid.NewGuid(),
            AppId: Guid.NewGuid(),
            ConfigVersionId: Guid.NewGuid(),
            Platform: BuildPlatform.Android,
            Type: BuildType.Debug);

        Directory.CreateDirectory(root);

        // ⚠️ Twenty megabytes across a few hundred entries, which is the shape
        // of a real shell APK rather than a convenient one. The cost of this
        // path is dominated by re-compressing every entry that is *not* being
        // replaced, so an archive with one big entry would measure nothing.
        var apkPath = await WriteApkAsync(entries: 400, bytesPerEntry: 50_000);
        var apkBytes = new FileInfo(apkPath).Length;

        var store = new FileSystemArtifactStore(Options.Create(new ArtifactStorageOptions
        {
            Directory = Path.Combine(root, "store"),
        }));

        var uploaded = await store.StoreAsync(request, apkPath);
        var sandbox = new RecordingSandbox(exitCode: 0);

        var patcher = new AndroidContentPatcher(
            store,
            sandbox,
            new AndroidSigningIdentity(
                Path.Combine(root, "debug.keystore"),
                "androiddebugkey",
                Path.Combine(root, "store.pw"),
                Path.Combine(root, "key.pw")));

        var lease = new RunnerLease(
            "perf",
            "runner",
            Path.Combine(root, "workspace"),
            Path.Combine(root, "cache"));

        var config = new JsonObject { ["app"] = new JsonObject { ["initialUrl"] = "https://after.example" } };

        var stopwatch = Stopwatch.StartNew();

        await patcher.PatchAsync(
            request,
            lease,
            new CacheLookup(CacheOutcome.Patch, uploaded.ArtifactReference, uploaded.Bytes),
            config,
            (line, isError, token) => Task.CompletedTask);

        stopwatch.Stop();

        Report(
            "content patch",
            $"{stopwatch.Elapsed.TotalSeconds:F2} s for a {apkBytes / 1024 / 1024:N0} MB APK "
            + "(fetch, rewrite, drop signature)");

        // ⚠️ Excludes zipalign and apksigner, which do not run here — there is
        // no Android SDK in this environment. What is measured is the part this
        // repository owns; the sprint review says so rather than presenting the
        // figure as an end-to-end build time.
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(30),
            "the whole point of the patch path is that it costs seconds where a compile costs minutes");
    }

    [Fact(DisplayName = "Reading a page of live log costs a millisecond or two")]
    public async Task ReadingLogsIsCheap()
    {
        var buildId = Guid.NewGuid();
        var options = Options.Create(new LogPipelineOptions
        {
            ArchiveRoot = Path.Combine(root, "logs"),
            BatchSize = 100,
            LiveStreamLines = 50_000,
            MaxBatchDelay = TimeSpan.FromHours(1),
        });

        await using (var pipeline = new RedisLogPipeline(
            redis.Connection,
            options,
            NullLogger<RedisLogPipeline>.Instance))
        {
            for (var index = 0; index < 5_000; index++)
            {
                await pipeline.AppendAsync(buildId, $"line {index}", isError: false);
            }

            await pipeline.ArchiveAsync(buildId);
        }

        var reader = new BuildLogReader(redis.Connection);
        var position = BuildLogReader.Beginning;
        var pages = 0;

        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var page = await reader.ReadAsync(buildId, position, count: 500);

            if (page.Lines.Length == 0)
            {
                break;
            }

            position = page.LastStreamId;
            pages++;
        }

        stopwatch.Stop();

        var perPage = stopwatch.Elapsed.TotalMilliseconds / pages;
        Report("log page read", $"{perPage:F2} ms per 500-line page over {pages} pages");

        // A viewer polls this while watching a build, and several people may
        // watch the same one.
        perPage.Should().BeLessThan(
            50,
            "a page read happens on every poll of every viewer of every running build");
    }

    private void Report(string what, string measurement) =>
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[perf] {what}: {measurement}"));

    private async Task<string> WriteApkAsync(int entries, int bytesPerEntry)
    {
        var path = Path.Combine(root, "cached.apk");
        var noise = new byte[bytesPerEntry];
        var random = new Random(20260902);

        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add(archive, "AndroidManifest.xml", "compiled-manifest");
            Add(archive, AndroidContentPatcher.ConfigEntryPath, """{"app":{"initialUrl":"https://before.example"}}""");
            Add(archive, "META-INF/MANIFEST.MF", "Name: classes.dex");
            Add(archive, "META-INF/CERT.RSA", "signature-block");

            for (var index = 0; index < entries; index++)
            {
                random.NextBytes(noise);

                await using var writing = await archive
                    .CreateEntry($"res/drawable/asset-{index}.bin", CompressionLevel.Fastest)
                    .OpenAsync();

                await writing.WriteAsync(noise);
            }
        }

        return path;

        static void Add(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
