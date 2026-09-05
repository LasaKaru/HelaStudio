using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Fleet;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Verification;
using Shellwright.Orchestrator.Workflows;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S08-BLD-054–066 — how the build activity executes a plan, and which
/// verifier an artifact reaches.
/// </summary>
/// <remarks>
/// ⚠️ The sandbox is a fake that records commands rather than running them, so
/// what is under test is the activity's own behaviour: that a four-step iOS
/// build stops at the step that failed, names it, writes the files the plan
/// asked for, and meters the whole plan rather than its last command. Whether
/// xcodebuild accepts the flags is a different question and needs a Mac.
/// </remarks>
public sealed class BuildActivityPlanTests : IDisposable
{
    private const string TeamId = "AB12CD34EF";

    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-plan-{Guid.NewGuid():N}");

    private readonly PlanRecordingSandbox sandbox = new();
    private readonly PlanRecordingLogs logs = new();

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnIosBuildRunsEveryStepInOrder()
    {
        var built = await BuildAsync(BuildPlatform.Ios);

        sandbox.Commands.Select(command => command.Executable)
            .Should().Equal("xcodebuild", "xcodegen", "xcodebuild", "xcodebuild");

        built.WasPatched.Should().BeFalse();
    }

    [Fact]
    public async Task TheExportOptionsPlistIsOnDiskBeforeTheFirstStepRuns()
    {
        var written = new List<bool>();
        var plistPath = Path.Combine(Workspace, BuildPlanner.IosExportOptionsPath);

        sandbox.OnRun = _ => written.Add(File.Exists(plistPath));

        await BuildAsync(BuildPlatform.Ios);

        // ⚠️ Before the *first* step, not merely before the export. xcodegen
        // reads the workspace, and a file appearing partway through a build is
        // a build whose output depends on when it looked.
        written.Should().OnlyContain(existed => existed);
        (await File.ReadAllTextAsync(plistPath)).Should().Contain(TeamId);
    }

    [Fact]
    public async Task AFailingStepStopsThePlanAndNamesItself()
    {
        // The archive: the third of four, so a plan that ran on would be
        // visible as an export attempted against an archive that does not exist.
        sandbox.FailAt = 2;

        var act = () => BuildAsync(BuildPlatform.Ios);

        var failure = (await act.Should().ThrowAsync<ApplicationFailureException>()).Which;

        failure.ErrorType.Should().Be(BuildFailures.CompilationFailed);
        failure.Message.Should().Contain("Archiving");
        sandbox.Commands.Should().HaveCount(3);
    }

    [Fact]
    public async Task EveryStepIsAnnouncedInTheBuildLog()
    {
        await BuildAsync(BuildPlatform.Ios);

        var announcements = logs.Lines.Where(line => line.StartsWith("==> ", StringComparison.Ordinal)).ToList();

        announcements.Should().HaveCount(4);
        announcements.Should().Contain("==> Archiving");
        announcements.Should().Contain("==> Exporting the IPA");
    }

    [Fact]
    public async Task MeteredTimeCoversThePlanRatherThanItsLastCommand()
    {
        sandbox.StepDuration = TimeSpan.FromSeconds(3);

        var ios = await BuildAsync(BuildPlatform.Ios);

        // Four steps at three seconds each. Metering the export alone would
        // bill three seconds for a build that took twelve, which is the whole
        // cost of an iOS build given away.
        ios.RunnerSeconds.Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public async Task AnAndroidBuildIsStillOneCommand()
    {
        var built = await BuildAsync(BuildPlatform.Android);

        sandbox.Commands.Should().ContainSingle()
            .Which.Executable.Should().Be("./gradlew");

        built.ArtifactPath.Should().EndWith(Path.Combine("apk", "release"));
    }

    [Fact]
    public async Task AnIosBuildWithNoAppleTeamFailsPermanentlyRatherThanQueueing()
    {
        var act = () => BuildAsync(BuildPlatform.Ios, teamId: null);

        var failure = (await act.Should().ThrowAsync<ApplicationFailureException>()).Which;

        // ⚠️ PlatformUnavailable and non-retryable. RunnerUnavailable would
        // tell the customer to wait for a slot that would not have helped, and
        // Temporal would retry it five times first.
        failure.ErrorType.Should().Be(BuildFailures.PlatformUnavailable);
        failure.NonRetryable.Should().BeTrue();
        sandbox.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task PlatformUnavailableIsInTheWorkflowsNonRetryableSet()
    {
        await Task.CompletedTask;

        // The type string is the only contract between the throw site and the
        // retry policy; a failure not in this list is retried three times.
        BuildFailures.NonRetryable.Should().Contain(BuildFailures.PlatformUnavailable);
    }

    [Fact]
    public async Task AnIpaIsVerifiedAsAnIpaAndNotAsAnApk()
    {
        var ipa = await WriteIpaAsync();
        var verifier = new PlatformArtifactVerifier(AndroidVerifier(), IosVerifier());

        var asIos = await verifier.VerifyAsync(Request(BuildPlatform.Ios), ipa);
        var asAndroid = await verifier.VerifyAsync(Request(BuildPlatform.Android), ipa);

        // ⚠️ The same bytes, routed by platform. Before the dispatcher existed
        // both of these took the Android path, so a real iOS build was rejected
        // for having no AndroidManifest.xml.
        asIos.Accepted.Should().BeTrue();
        asAndroid.Accepted.Should().BeFalse();
        asAndroid.Reason.Should().NotContain("iOS verifier");
    }

    [Fact]
    public async Task APlatformNothingCanVerifyIsRejectedRatherThanPassed()
    {
        var ipa = await WriteIpaAsync();
        var verifier = new PlatformArtifactVerifier(AndroidVerifier(), IosVerifier());

        var verdict = await verifier.VerifyAsync(Request((BuildPlatform)99), ipa);

        verdict.Accepted.Should().BeFalse();
    }

    private string Workspace => Path.Combine(root, "workspace");

    private async Task<BuiltArtifact> BuildAsync(BuildPlatform platform, string? teamId = TeamId)
    {
        Directory.CreateDirectory(Workspace);

        var request = Request(platform);
        var lease = new RunnerLease("lease-1", "runner-1", Workspace, Path.Combine(root, "cache"));
        var project = new GeneratedProject(Path.Combine(Workspace, "project"), 12);

        var options = Options.Create(new IosBuildOptions { TeamId = teamId });

        var activities = new BuildActivities(
            new PlanStubStore(),
            new PlanStubCache(),
            new PlanStubRunners(),
            sandbox,
            new PlanStubGenerator(project),
            new PlatformArtifactVerifier(AndroidVerifier(), IosVerifier()),
            new PlanStubPatcher(),
            new PlanStubArtifacts(),
            logs,
            new BuildToolchains(options),
            new BuildPlanner(options));

        return await new ActivityEnvironment().RunAsync(
            () => activities.BuildAsync(request, lease, project, CacheLookup.Miss));
    }

    private static BuildRequest Request(BuildPlatform platform) => new(
        BuildId: Guid.NewGuid(),
        OrgId: Guid.NewGuid(),
        AppId: Guid.NewGuid(),
        ConfigVersionId: Guid.NewGuid(),
        Platform: platform,
        Type: BuildType.Release);

    private static VerificationOptions Budgets() => new()
    {
        MaxArtifactBytes = 100 * 1024 * 1024,
        MinArtifactBytes = 1024,
    };

    private static AndroidArtifactVerifier AndroidVerifier() => new(Options.Create(Budgets()));

    private static IosArtifactVerifier IosVerifier() => new(Options.Create(Budgets()));

    private async Task<string> WriteIpaAsync()
    {
        Directory.CreateDirectory(root);

        var path = Path.Combine(root, $"fixture-{Guid.NewGuid():N}.ipa");

        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            Add(archive, "Payload/Acme.app/Info.plist", "<plist/>");
            Add(archive, "Payload/Acme.app/embedded.mobileprovision", "profile-bytes");
            Add(archive, "Payload/Acme.app/Acme", "mach-o-bytes");

            var noise = new byte[8_000];
            new Random(20260903).NextBytes(noise);

            await using var writing = await archive.CreateEntry("Symbols/pad.bin").OpenAsync();
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

/// <summary>A sandbox that records what it was asked to run without running it.</summary>
internal sealed class PlanRecordingSandbox : IBuildSandbox
{
    private readonly List<SandboxCommand> commands = [];

    public bool IsIsolated => false;

    public IReadOnlyList<SandboxCommand> Commands => commands;

    /// <summary>Zero-based index of the step that should fail, if any.</summary>
    public int? FailAt { get; set; }

    /// <summary>How long each step claims to have taken.</summary>
    public TimeSpan StepDuration { get; set; } = TimeSpan.FromMilliseconds(1);

    /// <summary>Called with each command before it is recorded.</summary>
    public Action<SandboxCommand>? OnRun { get; set; }

    public Task<RunnerLease> PrepareAsync(
        BuildRequest request,
        RunnerLease lease,
        CancellationToken cancellationToken = default) => Task.FromResult(lease);

    public Task<SandboxResult> RunAsync(
        RunnerLease lease,
        SandboxCommand command,
        LogLineHandler onLine,
        Action? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        OnRun?.Invoke(command);

        var index = commands.Count;
        commands.Add(command);
        onProgress?.Invoke();

        return Task.FromResult(new SandboxResult(FailAt == index ? 65 : 0, StepDuration));
    }

    public Task DestroyAsync(RunnerLease lease, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>A log pipeline that keeps its lines.</summary>
internal sealed class PlanRecordingLogs : IBuildLogPipeline
{
    private readonly ConcurrentQueue<string> lines = new();

    public IReadOnlyList<string> Lines => [.. lines];

    public Task AppendAsync(Guid buildId, string line, bool isError, CancellationToken cancellationToken = default)
    {
        lines.Enqueue(line);
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(Guid buildId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class PlanStubStore : IBuildStore
{
    public Task<StoredConfig?> LoadConfigAsync(Guid configVersionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<StoredConfig?>(new StoredConfig(Guid.NewGuid(), new JsonObject()));

    public Task RecordTransitionAsync(Guid buildId, BuildState state, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordFailureAsync(Guid buildId, BuildFailure failure, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordArtifactAsync(Guid buildId, UploadedArtifact artifact, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

internal sealed class PlanStubCache : IArtifactCache
{
    public Task<CacheLookup> LookupAsync(
        Guid appId,
        BuildPlatform platform,
        BuildType type,
        BuildHashes hashes,
        CancellationToken cancellationToken = default) => Task.FromResult(CacheLookup.Miss);

    public Task StoreAsync(
        Guid appId,
        BuildPlatform platform,
        BuildType type,
        BuildHashes hashes,
        UploadedArtifact artifact,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class PlanStubRunners : IRunnerPool
{
    public Task<RunnerLease?> TryLeaseAsync(BuildRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<RunnerLease?>(null);

    public Task RenewAsync(RunnerLease lease, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ReleaseAsync(RunnerLease lease, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class PlanStubGenerator(GeneratedProject project) : IProjectGenerator
{
    public Task<GeneratedProject> GenerateAsync(
        GenerationRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult(project);
}

internal sealed class PlanStubPatcher : IArtifactPatcher
{
    public bool Supports(BuildPlatform platform) => false;

    public Task<BuiltArtifact> PatchAsync(
        BuildRequest request,
        RunnerLease lease,
        CacheLookup cached,
        JsonObject resolved,
        LogLineHandler onLine,
        CancellationToken cancellationToken = default) =>
        throw new PatchNotPossibleException("Nothing here patches.");
}

internal sealed class PlanStubArtifacts : IArtifactStore
{
    public Task<UploadedArtifact> StoreAsync(
        BuildRequest request,
        string artifactPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new UploadedArtifact("artifact://sha256-" + new string('0', 64), 1));

    public Task<long> FetchAsync(
        string artifactReference,
        string destinationPath,
        CancellationToken cancellationToken = default) => Task.FromResult(0L);
}
