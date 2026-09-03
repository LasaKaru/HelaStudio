using System.Diagnostics;
using Shellwright.ConfigSchema;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;
using Temporalio.Activities;

namespace Shellwright.Orchestrator.Activities;

/// <summary>
/// Everything the build workflow is not allowed to do itself.
/// </summary>
/// <remarks>
/// The workflow is replayed, so it contains no clocks, no randomness, and no
/// I/O. All of that lives here, where Temporal records the result once and
/// replays the recording rather than the work.
/// </remarks>
/// <param name="store">Reads configurations, writes build records and usage.</param>
/// <param name="cache">Finds and stores artifacts by cache key.</param>
/// <param name="runners">Hands out runner slots.</param>
/// <param name="sandbox">Runs the toolchain in isolation.</param>
/// <param name="generator">Turns a configuration into a project.</param>
/// <param name="verifier">Checks what the toolchain produced.</param>
/// <param name="patcher">Rebuilds a cached artifact when only content changed.</param>
/// <param name="artifacts">Stores the finished artifact.</param>
/// <param name="logs">Streams and archives build output.</param>
/// <param name="toolchains">Which toolchain each platform builds with, and so what its cache key says.</param>
/// <param name="planner">Turns a request into the commands that produce its artifact.</param>
public sealed class BuildActivities(
    IBuildStore store,
    IArtifactCache cache,
    IRunnerPool runners,
    IBuildSandbox sandbox,
    IProjectGenerator generator,
    IArtifactVerifier verifier,
    IArtifactPatcher patcher,
    IArtifactStore artifacts,
    IBuildLogPipeline logs,
    BuildToolchains toolchains,
    BuildPlanner planner)
{
    /// <summary>How often the long build activity reports progress.</summary>
    /// <remarks>
    /// Six times inside the sixty-second heartbeat timeout, so a runner has to
    /// miss several before Temporal gives up on it. One beat per timeout would
    /// make an ordinary scheduling hiccup look like a dead runner.
    /// </remarks>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    /// <summary>Re-runs validation on the server, and computes the cache keys.</summary>
    /// <param name="request">The build.</param>
    /// <returns>What validation found.</returns>
    /// <remarks>
    /// ⚠️ Re-run rather than trusted. The studio validates, the API validates on
    /// save, and this validates again — not because the earlier two are
    /// unreliable, but because neither of them is what a build request has to
    /// go through. A build can be started by an API token against a version
    /// saved months ago under an older rule set.
    /// </remarks>
    [Activity(BuildActivityNames.Validate)]
    public async Task<ValidationOutcome> ValidateAsync(BuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await store.LoadConfigAsync(request.ConfigVersionId, ActivityExecutionContext.Current.CancellationToken)
            ?? throw BuildFailures.Permanent(
                BuildFailures.ConfigInvalid,
                $"Configuration version {request.ConfigVersionId} does not exist.");

        if (stored.AppId != request.AppId)
        {
            // The API should never produce this, which is exactly why it is
            // checked: a build that compiles one tenant's configuration under
            // another tenant's app is the worst bug this system could have.
            throw BuildFailures.Permanent(
                BuildFailures.ConfigInvalid,
                "That configuration version belongs to a different app.");
        }

        var validated = new ConfigValidator().Validate(stored.Body);

        if (!validated.Result.Valid)
        {
            var first = validated.Result.Errors[0];
            return new ValidationOutcome(
                false,
                $"{first.Code} at {first.Path}: {first.Message}",
                new BuildHashes(string.Empty, string.Empty, string.Empty));
        }

        var hashes = ConfigHasher.Compute(validated.Resolved, toolchains.HashContextFor(request.Platform));

        return new ValidationOutcome(
            true,
            string.Empty,
            new BuildHashes(hashes.CodeKey, hashes.AssetKey, hashes.ContentKey));
    }

    /// <summary>Looks for an artifact that can be reused.</summary>
    /// <param name="request">The build.</param>
    /// <param name="hashes">The cache keys.</param>
    /// <returns>How much can be reused.</returns>
    [Activity(BuildActivityNames.LookupCache)]
    public async Task<CacheLookup> LookupCacheAsync(BuildRequest request, BuildHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await cache.LookupAsync(
            request.AppId,
            request.Platform,
            request.Type,
            hashes,
            ActivityExecutionContext.Current.CancellationToken);
    }

    /// <summary>Takes a runner slot.</summary>
    /// <param name="request">The build.</param>
    /// <returns>The lease.</returns>
    [Activity(BuildActivityNames.LeaseRunner)]
    public async Task<Workflows.RunnerLease> LeaseRunnerAsync(BuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lease = await runners.TryLeaseAsync(request, ActivityExecutionContext.Current.CancellationToken);

        // Retryable: the fleet being full is a condition that passes. The
        // workflow's retry policy backs off, which is the behaviour a queue
        // would have given us anyway, without the queue.
        return lease ?? throw BuildFailures.Transient(
            BuildFailures.RunnerUnavailable,
            "No runner slot is free. Waiting for one to be released.");
    }

    /// <summary>Generates the platform project from the configuration.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="hashes">The cache keys, recorded into the generated manifest.</param>
    /// <returns>What was generated.</returns>
    [Activity(BuildActivityNames.Generate)]
    public async Task<GeneratedProject> GenerateAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        BuildHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);

        var token = ActivityExecutionContext.Current.CancellationToken;

        var stored = await store.LoadConfigAsync(request.ConfigVersionId, token)
            ?? throw BuildFailures.Permanent(
                BuildFailures.ConfigInvalid,
                $"Configuration version {request.ConfigVersionId} disappeared mid-build.");

        var prepared = await sandbox.PrepareAsync(request, lease, token);

        return await generator.GenerateAsync(
            new GenerationRequest(stored.Body, request.Platform, prepared.WorkspaceRoot, hashes),
            token);
    }

    /// <summary>Runs the toolchain, or patches a cached artifact.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="project">What was generated.</param>
    /// <param name="cached">What the cache offered.</param>
    /// <returns>What was produced.</returns>
    [Activity(BuildActivityNames.Build)]
    public async Task<BuiltArtifact> BuildAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        GeneratedProject project,
        CacheLookup cached)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cached);

        var context = ActivityExecutionContext.Current;
        var token = context.CancellationToken;

        if (cached.Kind == CacheOutcome.Patch && patcher.Supports(request.Platform))
        {
            var patched = await TryPatchAsync(request, lease, cached, token);

            if (patched is not null)
            {
                return patched;
            }
        }

        var plan = Plan(request, lease, project);

        await WritePlannedFilesAsync(lease, plan, token);

        // ⚠️ Two clocks, deliberately. The stopwatch is wall time since the plan
        // began and feeds the heartbeat, which is about liveness. What is
        // metered is the sum of what the sandbox measured for each command —
        // the customer pays for the toolchain running, not for the
        // orchestrator's own file writes and scheduling between steps.
        var stopwatch = Stopwatch.StartNew();
        var metered = TimeSpan.Zero;

        foreach (var step in plan.Steps)
        {
            // Named in the log before its output, so a failure that produces
            // nothing legible still says which of the four things an iOS build
            // does was the one that stopped.
            await logs.AppendAsync(request.BuildId, $"==> {step.Name}", isError: false, token);

            var result = await sandbox.RunAsync(
                lease,
                step.Command,
                (line, isError, ct) => logs.AppendAsync(request.BuildId, line, isError, ct),

                // ⚠️ Heartbeating is what makes cancellation and dead-runner
                // detection work at all. Without it Temporal cannot tell a build
                // that is compiling from a runner that has stopped existing, and
                // waits out the twenty-minute timeout for both.
                //
                // ⚠️ Elapsed is the stopwatch across the whole plan rather than
                // one step, so the heartbeat payload keeps rising through a
                // four-step iOS build instead of resetting at each step.
                onProgress: () => context.Heartbeat(stopwatch.Elapsed.TotalSeconds),
                cancellationToken: token);

            metered += result.Duration;

            if (result.ExitCode != 0)
            {
                stopwatch.Stop();

                // ⚠️ Non-retryable. The same sources compiled by the same toolchain
                // fail the same way, and each attempt costs runner minutes somebody
                // is paying for.
                throw BuildFailures.Permanent(
                    BuildFailures.CompilationFailed,
                    $"{step.Name} exited with code {result.ExitCode}. The log says why.");
            }
        }

        stopwatch.Stop();

        return new BuiltArtifact(
            plan.ArtifactPath,

            // ⚠️ Every step, not the last one. Metering the final command alone
            // would bill an iOS build for its export and give away the
            // archive, which is where all of the cost is.
            (int)Math.Ceiling(metered.TotalSeconds),

            // ⚠️ False, unconditionally, and that is the point. Reaching here
            // means a compiler ran, whatever the cache said a moment ago —
            // including when a patch was attempted and turned out to be
            // impossible. Deriving this from the cache outcome instead would
            // meter a four-minute compile as a patched build.
            WasPatched: false);
    }

    /// <summary>
    /// Patches the cached artifact, or answers null when it cannot be patched.
    /// </summary>
    /// <remarks>
    /// ⚠️ Only <see cref="PatchNotPossibleException"/> is recovered from, and
    /// only into a full build. Anything else — a signing tool that fails, a
    /// runner with no disk — is a real failure and must surface, because
    /// falling back on those would turn a fleet that cannot sign anything into
    /// a fleet that is merely slow, and nobody would notice for weeks.
    /// </remarks>
    private async Task<BuiltArtifact?> TryPatchAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        CacheLookup cached,
        CancellationToken token)
    {
        var stored = await store.LoadConfigAsync(request.ConfigVersionId, token)
            ?? throw BuildFailures.Permanent(
                BuildFailures.ConfigInvalid,
                $"Configuration version {request.ConfigVersionId} disappeared mid-build.");

        var validated = new ConfigValidator().Validate(stored.Body);

        if (!validated.Result.Valid)
        {
            throw BuildFailures.Permanent(
                BuildFailures.ConfigInvalid,
                "The configuration stopped being valid between validation and build.");
        }

        try
        {
            return await patcher.PatchAsync(
                request,
                lease,
                cached,
                validated.Resolved,
                (line, isError, ct) => logs.AppendAsync(request.BuildId, line, isError, ct),
                token);
        }
        catch (PatchNotPossibleException exception)
        {
            await logs.AppendAsync(
                request.BuildId,
                $"The previous build could not be reused ({exception.Message}). Building in full.",
                isError: false,
                token);

            return null;
        }
    }

    /// <summary>Plans the build, turning a missing platform into a build failure.</summary>
    /// <remarks>
    /// ⚠️ Both refusals are permanent. A deployment with no Apple team
    /// configured, or a platform nobody has written a plan for, is a condition
    /// that does not pass on its own — retrying it three times only spends the
    /// customer's queue position on the same answer.
    /// </remarks>
    private BuildPlan Plan(BuildRequest request, Workflows.RunnerLease lease, GeneratedProject project)
    {
        try
        {
            return planner.Plan(request, lease, project);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            throw BuildFailures.Permanent(BuildFailures.PlatformUnavailable, exception.Message);
        }
    }

    /// <summary>Writes the files a plan needs before its first command runs.</summary>
    /// <remarks>
    /// ⚠️ Resolved against the workspace root and checked again afterwards.
    /// <see cref="PlannedFile"/> validates its own path, so this is a second
    /// lock on the same door — worth having, because this is the method that
    /// actually writes to the runner's disk.
    /// </remarks>
    private static async Task WritePlannedFilesAsync(
        Workflows.RunnerLease lease,
        BuildPlan plan,
        CancellationToken token)
    {
        if (plan.Files.IsEmpty)
        {
            return;
        }

        var root = Path.GetFullPath(lease.WorkspaceRoot);

        foreach (var file in plan.Files)
        {
            var destination = Path.GetFullPath(Path.Combine(root, file.RelativePath));

            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw BuildFailures.Permanent(
                    BuildFailures.CompilationFailed,
                    "A build step asked to write outside its workspace.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, file.Contents, token);
        }
    }

    /// <summary>Checks the signature, the manifest, the size, and the permissions.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="built">What was produced.</param>
    /// <returns>A task that completes when the artifact has been accepted.</returns>
    [Activity(BuildActivityNames.Verify)]
    public async Task VerifyAsync(BuildRequest request, Workflows.RunnerLease lease, BuiltArtifact built)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(built);

        var verdict = await verifier.VerifyAsync(
            request,
            built.ArtifactPath,
            ActivityExecutionContext.Current.CancellationToken);

        if (!verdict.Accepted)
        {
            throw BuildFailures.Permanent(BuildFailures.VerificationFailed, verdict.Reason);
        }
    }

    /// <summary>Stores the artifact and records it against its cache keys.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="built">What was produced.</param>
    /// <param name="hashes">The cache keys.</param>
    /// <returns>Where it ended up.</returns>
    [Activity(BuildActivityNames.Upload)]
    public async Task<UploadedArtifact> UploadAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        BuiltArtifact built,
        BuildHashes hashes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(built);

        var token = ActivityExecutionContext.Current.CancellationToken;

        var uploaded = await artifacts.StoreAsync(request, built.ArtifactPath, token);

        await store.RecordArtifactAsync(request.BuildId, uploaded, token);
        await cache.StoreAsync(request.AppId, request.Platform, request.Type, hashes, uploaded, token);
        await logs.ArchiveAsync(request.BuildId, token);

        return uploaded;
    }

    /// <summary>Records that a build changed state.</summary>
    /// <param name="buildId">The build.</param>
    /// <param name="state">Where it moved to.</param>
    /// <returns>A task that completes when the transition is durable.</returns>
    [Activity(BuildActivityNames.RecordTransition)]
    public async Task RecordTransitionAsync(Guid buildId, BuildState state) =>
        await store.RecordTransitionAsync(buildId, state, ActivityExecutionContext.Current.CancellationToken);

    /// <summary>Records metered usage.</summary>
    /// <param name="usage">What to charge for.</param>
    /// <returns>A task that completes when the row is durable.</returns>
    [Activity(BuildActivityNames.RecordUsage)]
    public async Task RecordUsageAsync(UsageRecord usage) =>
        await store.RecordUsageAsync(usage, ActivityExecutionContext.Current.CancellationToken);

    /// <summary>Destroys the workspace and returns the runner slot.</summary>
    /// <param name="lease">The lease.</param>
    /// <returns>A task that completes once the slot is free.</returns>
    /// <remarks>
    /// ⚠️ Destroys first, releases second. Releasing a slot whose workspace
    /// still exists offers the next tenant a directory full of somebody else's
    /// source.
    /// </remarks>
    [Activity(BuildActivityNames.ReleaseRunner)]
    public async Task ReleaseRunnerAsync(Workflows.RunnerLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        var token = ActivityExecutionContext.Current.CancellationToken;

        await sandbox.DestroyAsync(lease, token);
        await runners.ReleaseAsync(lease, token);
    }
}
