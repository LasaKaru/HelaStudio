using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Fleet;
using Shellwright.Orchestrator.Tests.Infrastructure;
using Shellwright.Orchestrator.Verification;
using Shellwright.Orchestrator.Workflows;
using Xunit;
using Xunit.Abstractions;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S08-PERF-001–004 — what Sprint 08's hot paths cost.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Budgets, not benchmarks, on the same terms as
/// <see cref="BuildPerformanceTests"/>: a ceiling with several times the
/// measured headroom, so it is a regression alarm rather than a flaky
/// micro-measurement.
/// </para>
/// <para>
/// ⚠️ None of these is an iOS build. What is measured is what runs on the
/// orchestrator regardless of platform — planning, verifying an IPA's bytes,
/// placing work on a fleet, and moving an artifact through an S3 endpoint on
/// loopback. The archive and export themselves need a Mac, and their cost is
/// unknown here rather than estimated.
/// </para>
/// </remarks>
/// <param name="output">Where measurements are written.</param>
public sealed class IosPerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly FakeObjectStore endpoint = new();
    private readonly List<IDisposable> clients = [];

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-iosperf-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        endpoint.Dispose();

        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Verifying a 60 MB IPA takes milliseconds, not seconds")]
    public async Task VerifyingAnIpaIsCheap()
    {
        var ipa = await WriteIpaAsync(60 * 1024 * 1024);

        var verifier = new IosArtifactVerifier(Options.Create(new VerificationOptions
        {
            MaxArtifactBytes = 200L * 1024 * 1024,
            MinArtifactBytes = 1024,
        }));

        var request = Request(BuildPlatform.Ios);

        // Warm the file cache, so what is measured is the verifier rather than
        // the first read of a freshly written file.
        await verifier.VerifyAsync(request, ipa);

        var stopwatch = Stopwatch.StartNew();
        var verdict = await verifier.VerifyAsync(request, ipa);
        stopwatch.Stop();

        verdict.Accepted.Should().BeTrue();

        Report("IPA verification, 60 MB", stopwatch.Elapsed.TotalMilliseconds, "ms");

        // ⚠️ This runs on the critical path of every build, between the
        // toolchain finishing and the customer being told. It reads the zip
        // directory rather than the entries, which is why it is cheap; a
        // version that decompressed would not be.
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "verification sits between the build finishing and the customer hearing about it");
    }

    [Fact(DisplayName = "Placing a build on a hundred-host fleet is instant")]
    public void FleetPlacementIsCheap()
    {
        var hosts = Enumerable.Range(0, 100)
            .Select(index => new MacHost(
                HostId: string.Create(CultureInfo.InvariantCulture, $"mac-{index}"),
                Provider: "test",
                XcodeVersions: ["16.2", "26.1"],
                State: HostState.Healthy,
                ActiveBuilds: index % 2,
                LastHealthyAt: DateTimeOffset.UtcNow))
            .ToList();

        var stopwatch = Stopwatch.StartNew();

        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            MacFleet.Place(hosts, "16.2");
        }

        stopwatch.Stop();

        var perPlacement = stopwatch.Elapsed.TotalMilliseconds / 1_000;

        Report("Fleet placement, 100 hosts", perPlacement, "ms");

        // Placement runs while a customer is waiting for a slot, and it is pure
        // computation over a list. A budget here catches somebody turning it
        // into a query.
        perPlacement.Should().BeLessThan(
            1.0,
            "placement is a scan over a list and must not become a round trip");
    }

    [Fact(DisplayName = "Planning a build costs nothing measurable")]
    public void PlanningIsFree()
    {
        var options = Options.Create(new IosBuildOptions { TeamId = "AB12CD34EF" });
        var planner = new BuildPlanner(options);

        var request = Request(BuildPlatform.Ios);
        var lease = new RunnerLease("lease", "runner", Path.Combine(root, "w"), Path.Combine(root, "c"));
        var project = new GeneratedProject(Path.Combine(root, "w", "p"), 40);

        var stopwatch = Stopwatch.StartNew();

        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            planner.Plan(request, lease, project);
        }

        stopwatch.Stop();

        var perPlan = stopwatch.Elapsed.TotalMilliseconds / 10_000;

        Report("Build planning", perPlan, "ms");

        // ⚠️ The point of this budget is not speed — it is that planning stays
        // pure. A plan that read a file or asked a service would not come in
        // under a hundredth of a millisecond, and would no longer be testable
        // without the thing it reached for.
        perPlan.Should().BeLessThan(
            0.05,
            "planning must stay a pure function of its inputs");
    }

    [Fact(DisplayName = "A 60 MB artifact streams through object storage in seconds")]
    public async Task ObjectStorageThroughput()
    {
        var settings = new ObjectStorageOptions
        {
            ServiceUrl = endpoint.ServiceUrl,
            Bucket = "shellwright-artifacts",
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            MaxArtifactBytes = 2_000_000_000,
        };

        var client = ObjectStoreClientFactory.Create(settings);
        clients.Add(client);

        var store = new ObjectStoreArtifactStore(client, Options.Create(settings));

        const int size = 60 * 1024 * 1024;
        var source = await WriteArtifactAsync(size);
        var request = Request(BuildPlatform.Ios);

        var upload = Stopwatch.StartNew();
        var uploaded = await store.StoreAsync(request, source);
        upload.Stop();

        var destination = Path.Combine(root, "fetched.ipa");

        var download = Stopwatch.StartNew();
        var bytes = await store.FetchAsync(uploaded.ArtifactReference, destination);
        download.Stop();

        bytes.Should().Be(size);

        Report("Artifact upload, 60 MB (loopback)", upload.Elapsed.TotalSeconds, "s");
        Report("Artifact download, 60 MB (loopback)", download.Elapsed.TotalSeconds, "s");

        // ⚠️ Loopback, so this is not R2 and says nothing about R2. What it
        // does catch is the store buffering an artifact whole instead of
        // streaming it, which on a 60 MB IPA is the difference between a
        // constant footprint and one allocation per concurrent build on the
        // large object heap.
        upload.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        download.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private void Report(string what, double value, string unit) =>
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what}: {value:F3} {unit}"));

    private static BuildRequest Request(BuildPlatform platform) => new(
        BuildId: Guid.NewGuid(),
        OrgId: Guid.NewGuid(),
        AppId: Guid.NewGuid(),
        ConfigVersionId: Guid.NewGuid(),
        Platform: platform,
        Type: BuildType.Release);

    private async Task<string> WriteArtifactAsync(int bytes)
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, "artifact.ipa");
        var content = new byte[bytes];

        for (var index = 0; index < content.Length; index++)
        {
            content[index] = (byte)(index % 251);
        }

        await File.WriteAllBytesAsync(path, content);

        return path;
    }

    private async Task<string> WriteIpaAsync(int approximateBytes)
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, "large.ipa");

        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add(archive, "Payload/Acme.app/Info.plist", "<plist/>");
            Add(archive, "Payload/Acme.app/embedded.mobileprovision", "profile-bytes");
            Add(archive, "Payload/Acme.app/Acme", "mach-o-bytes");

            // Incompressible, so the archive is genuinely the size claimed
            // rather than a few kilobytes of zeroes that deflate away.
            var noise = new byte[approximateBytes];
            new Random(20260903).NextBytes(noise);

            await using var writing = await archive.CreateEntry(
                "Payload/Acme.app/Frameworks/big.dylib",
                CompressionLevel.NoCompression).OpenAsync();

            await writing.WriteAsync(noise);
        }

        return path;

        static void Add(ZipArchive archive, string path, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(path).Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }
}
