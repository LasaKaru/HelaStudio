using System.Collections.Concurrent;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;
using Temporalio.Activities;

namespace Shellwright.Orchestrator.Tests.Infrastructure;

/// <summary>
/// Activities the workflow tests drive directly.
/// </summary>
/// <remarks>
/// ⚠️ Registered under the same explicit names as the real activities. The
/// first version of this file relied on Temporal's derived names and the suite
/// failed with "Activity Validate is not registered": the real method returns a
/// task and had its Async suffix stripped, this one did not. That is the same
/// mismatch a deploy would produce, which is why the names are now constants.
///
/// ⚠️ These stand in for the real activities, not for the workflow. What is
/// under test here is the workflow's control flow — that a compilation failure
/// is attempted once, that a cancelled build still releases its runner, that
/// the cache short-circuits — and none of that depends on what Gradle does.
/// Driving the real activities would mean every one of these tests needed an
/// Android toolchain to check a branch.
/// </remarks>
public sealed class FakeActivities
{
    private readonly ConcurrentQueue<string> calls = new();
    private readonly ConcurrentQueue<BuildState> transitions = new();
    private readonly ConcurrentQueue<UsageRecord> usage = new();

    /// <summary>Every activity that ran, in order.</summary>
    public IReadOnlyList<string> Calls => [.. calls];

    /// <summary>How many times the build activity was attempted.</summary>
    public int BuildAttempts { get; private set; }

    /// <summary>What validation should report.</summary>
    public ValidationOutcome Validation { get; set; } =
        new(true, string.Empty, new BuildHashes("code", "asset", "content"));

    /// <summary>What the cache should report.</summary>
    public CacheLookup Cache { get; set; } = CacheLookup.Miss;

    /// <summary>When set, the build activity throws this.</summary>
    public Func<Exception>? BuildThrows { get; set; }

    /// <summary>When set, the build activity waits for cancellation rather than returning.</summary>
    public bool BuildBlocks { get; set; }

    /// <summary>Completes once the build activity has started.</summary>
    public TaskCompletionSource BuildStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>States the build was recorded as moving through.</summary>
    public IReadOnlyList<BuildState> Transitions => [.. transitions];

    /// <summary>Usage rows written.</summary>
    public IReadOnlyList<UsageRecord> Usage => [.. usage];

    /// <summary>Stands in for server-side validation.</summary>
    /// <param name="request">The build.</param>
    /// <returns>The configured outcome.</returns>
    [Activity(BuildActivityNames.Validate)]
    public ValidationOutcome ValidateAsync(BuildRequest request)
    {
        calls.Enqueue(nameof(ValidateAsync));
        return Validation;
    }

    /// <summary>Stands in for the cache lookup.</summary>
    /// <param name="request">The build.</param>
    /// <param name="hashes">The cache keys.</param>
    /// <returns>The configured outcome.</returns>
    [Activity(BuildActivityNames.LookupCache)]
    public CacheLookup LookupCacheAsync(BuildRequest request, BuildHashes hashes)
    {
        calls.Enqueue(nameof(LookupCacheAsync));
        return Cache;
    }

    /// <summary>Stands in for leasing a runner.</summary>
    /// <param name="request">The build.</param>
    /// <returns>A lease.</returns>
    [Activity(BuildActivityNames.LeaseRunner)]
    public RunnerLease LeaseRunnerAsync(BuildRequest request)
    {
        calls.Enqueue(nameof(LeaseRunnerAsync));
        return new RunnerLease("lease-1", "runner-1", "/workspace", "/cache");
    }

    /// <summary>Stands in for generation.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="hashes">The cache keys.</param>
    /// <returns>A generated project.</returns>
    [Activity(BuildActivityNames.Generate)]
    public GeneratedProject GenerateAsync(BuildRequest request, RunnerLease lease, BuildHashes hashes)
    {
        calls.Enqueue(nameof(GenerateAsync));
        return new GeneratedProject("/workspace", 46);
    }

    /// <summary>Stands in for the toolchain.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="project">The generated project.</param>
    /// <param name="cached">What the cache offered.</param>
    /// <returns>A built artifact.</returns>
    [Activity(BuildActivityNames.Build)]
    public async Task<BuiltArtifact> BuildAsync(
        BuildRequest request,
        RunnerLease lease,
        GeneratedProject project,
        CacheLookup cached)
    {
        calls.Enqueue(nameof(BuildAsync));
        BuildAttempts++;
        BuildStarted.TrySetResult();

        if (BuildThrows is { } thrower)
        {
            throw thrower();
        }

        if (BuildBlocks)
        {
            var token = ActivityExecutionContext.Current.CancellationToken;

            // Heartbeats while waiting, exactly as the real activity does, so
            // the cancellation path under test is the real one.
            while (!token.IsCancellationRequested)
            {
                ActivityExecutionContext.Current.Heartbeat();
                await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            }

            token.ThrowIfCancellationRequested();
        }

        return new BuiltArtifact("/workspace/app.apk", 42, cached.Kind == CacheOutcome.Patch);
    }

    /// <summary>Stands in for verification.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="built">What was produced.</param>
    [Activity(BuildActivityNames.Verify)]
    public void VerifyAsync(BuildRequest request, RunnerLease lease, BuiltArtifact built) =>
        calls.Enqueue(nameof(VerifyAsync));

    /// <summary>Stands in for upload.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot.</param>
    /// <param name="built">What was produced.</param>
    /// <param name="hashes">The cache keys.</param>
    /// <returns>Where it went.</returns>
    [Activity(BuildActivityNames.Upload)]
    public UploadedArtifact UploadAsync(
        BuildRequest request,
        RunnerLease lease,
        BuiltArtifact built,
        BuildHashes hashes)
    {
        calls.Enqueue(nameof(UploadAsync));
        return new UploadedArtifact("artifact://sha256-abc", 1234);
    }

    /// <summary>Records a state transition.</summary>
    /// <param name="buildId">The build.</param>
    /// <param name="state">Where it moved to.</param>
    [Activity(BuildActivityNames.RecordTransition)]
    public void RecordTransitionAsync(Guid buildId, BuildState state)
    {
        calls.Enqueue($"{nameof(RecordTransitionAsync)}:{state}");
        transitions.Enqueue(state);
    }

    /// <summary>Records metered usage.</summary>
    /// <param name="record">What to charge for.</param>
    [Activity(BuildActivityNames.RecordUsage)]
    public void RecordUsageAsync(UsageRecord record)
    {
        calls.Enqueue(nameof(RecordUsageAsync));
        usage.Enqueue(record);
    }

    /// <summary>Releases the runner slot.</summary>
    /// <param name="lease">The lease.</param>
    [Activity(BuildActivityNames.ReleaseRunner)]
    public void ReleaseRunnerAsync(RunnerLease lease) => calls.Enqueue(nameof(ReleaseRunnerAsync));
}
