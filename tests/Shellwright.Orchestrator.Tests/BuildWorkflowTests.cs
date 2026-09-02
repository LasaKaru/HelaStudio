using FluentAssertions;
using Shellwright.Orchestrator.Tests.Infrastructure;
using Shellwright.Orchestrator.Workflows;
using Temporalio.Client;
using Temporalio.Exceptions;
using Temporalio.Worker;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// The workflow's control flow, against a real Temporal server.
/// </summary>
/// <remarks>
/// ⚠️ A real server rather than a hand-rolled harness, because what is being
/// checked *is* Temporal's behaviour: that a non-retryable failure is attempted
/// once, that cancellation reaches an activity and still runs the compensation,
/// that a workflow survives being replayed. A fake would agree with whatever
/// this file asserted.
/// </remarks>
[Collection(TemporalFixtureDefinition.Name)]
public sealed class BuildWorkflowTests(TemporalFixture temporal)
{
    private static readonly BuildRequest Request = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        BuildPlatform.Android,
        BuildType.Release);

    /// <summary>The happy path runs every stage in order and meters the result.</summary>
    [Fact]
    public async Task A_successful_build_visits_every_stage()
    {
        var activities = new FakeActivities();

        var result = await RunAsync(activities);

        result.State.Should().Be(BuildState.Succeeded);
        result.ArtifactReference.Should().Be("artifact://sha256-abc");
        result.CacheHit.Should().BeFalse();

        activities.Transitions.Should().Equal(
            BuildState.Generating,
            BuildState.Building,
            BuildState.Verifying,
            BuildState.Succeeded);

        activities.Usage.Should().ContainSingle();
        activities.Usage[0].RunnerSeconds.Should().Be(42);
        activities.Usage[0].CacheHit.Should().BeFalse();
    }

    /// <summary>The runner is always released, on every path.</summary>
    [Fact]
    public async Task A_successful_build_releases_its_runner()
    {
        var activities = new FakeActivities();

        await RunAsync(activities);

        activities.Calls.Should().Contain(nameof(FakeActivities.ReleaseRunnerAsync));
    }

    /// <summary>An invalid configuration never leases a runner.</summary>
    /// <remarks>
    /// ⚠️ The cheap check runs first for a reason: leasing a runner to discover
    /// a typo is the most expensive way to find one.
    /// </remarks>
    [Fact]
    public async Task An_invalid_configuration_never_reaches_a_runner()
    {
        var activities = new FakeActivities
        {
            Validation = new ValidationOutcome(
                false,
                "CFG_BUNDLE_ID_INVALID at /app/bundleId",
                new BuildHashes(string.Empty, string.Empty, string.Empty)),
        };

        var result = await RunAsync(activities);

        result.State.Should().Be(BuildState.Failed);
        result.Failure!.Code.Should().Be("BLD_CONFIG_INVALID");

        activities.Calls.Should().NotContain(nameof(FakeActivities.LeaseRunnerAsync));
        activities.Calls.Should().NotContain(nameof(FakeActivities.BuildAsync));
    }

    /// <summary>A complete cache hit returns the artifact without touching a runner.</summary>
    [Fact]
    public async Task A_complete_cache_hit_skips_the_build_entirely()
    {
        var activities = new FakeActivities
        {
            Cache = new CacheLookup(CacheOutcome.Complete, "artifact://sha256-cached", 4321),
        };

        var result = await RunAsync(activities);

        result.State.Should().Be(BuildState.Succeeded);
        result.CacheHit.Should().BeTrue();
        result.ArtifactReference.Should().Be("artifact://sha256-cached");
        result.RunnerSeconds.Should().Be(0);

        activities.Calls.Should().NotContain(nameof(FakeActivities.LeaseRunnerAsync));
        activities.Calls.Should().NotContain(nameof(FakeActivities.BuildAsync));

        // ⚠️ Still metered, at zero runner seconds. A build that produced an
        // artifact and left no record is a build nobody can account for, even
        // when it cost nothing.
        activities.Usage.Should().ContainSingle();
        activities.Usage[0].CacheHit.Should().BeTrue();
        activities.Usage[0].RunnerSeconds.Should().Be(0);
    }

    /// <summary>A patchable hit still runs the build, and reports itself as a hit.</summary>
    [Fact]
    public async Task A_patchable_cache_hit_still_builds_but_counts_as_a_hit()
    {
        var activities = new FakeActivities
        {
            Cache = new CacheLookup(CacheOutcome.Patchable, "artifact://sha256-old", 4321),
        };

        var result = await RunAsync(activities);

        result.State.Should().Be(BuildState.Succeeded);
        result.CacheHit.Should().BeTrue();
        activities.Calls.Should().Contain(nameof(FakeActivities.BuildAsync));
    }

    /// <summary>
    /// TC-S07-BLD-008 — a compilation failure is attempted once and not retried.
    /// </summary>
    /// <remarks>
    /// ⚠️ The single most expensive bug this sprint could ship. The same
    /// sources compiled by the same toolchain fail identically, and each retry
    /// costs runner minutes somebody is paying for. Three attempts on every
    /// broken build would triple the cost of the most common failure.
    /// </remarks>
    [Fact]
    public async Task A_compilation_failure_is_not_retried()
    {
        var activities = new FakeActivities
        {
            BuildThrows = () => BuildFailures.Permanent(
                BuildFailures.CompilationFailed,
                "The build exited with code 1."),
        };

        var result = await RunAsync(activities);

        result.State.Should().Be(BuildState.Failed);
        result.Failure!.Code.Should().Be(BuildFailures.CompilationFailed);

        activities.BuildAttempts.Should().Be(1, "a compilation failure will fail identically every time");
        activities.Calls.Should().Contain(nameof(FakeActivities.ReleaseRunnerAsync));
    }

    /// <summary>
    /// TC-S07-BLD-009 — an infrastructure failure is retried.
    /// </summary>
    [Fact]
    public async Task An_infrastructure_failure_is_retried()
    {
        var attempts = 0;

        var activities = new FakeActivities
        {
            BuildThrows = () =>
            {
                attempts++;
                return BuildFailures.Transient(BuildFailures.StorageUnavailable, "R2 returned 503.");
            },
        };

        var result = await RunAsync(activities);

        result.State.Should().Be(BuildState.Failed);

        // Three, from the policy — not one, and not forever.
        attempts.Should().Be(3);
        activities.Calls.Should().Contain(nameof(FakeActivities.ReleaseRunnerAsync));
    }

    /// <summary>
    /// TC-S07-BLD-006 — cancellation reaches the activity and still frees the runner.
    /// </summary>
    /// <remarks>
    /// ⚠️ The compensation is the part worth proving. A cancelled workflow
    /// cancels every activity it starts, including the one that releases the
    /// lease — so without an explicit uncancellable token the runner would be
    /// held until its lease expired, and the fleet would lose a slot to every
    /// cancelled build.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_build_releases_its_runner()
    {
        var activities = new FakeActivities { BuildBlocks = true };

        using var worker = new TemporalWorker(
            temporal.Server.Client,
            new TemporalWorkerOptions(BuildWorkflow.TaskQueue)
                .AddAllActivities(activities)
                .AddWorkflow<BuildWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await temporal.Server.Client.StartWorkflowAsync(
                (BuildWorkflow w) => w.RunAsync(Request),
                new WorkflowOptions(Guid.NewGuid().ToString(), BuildWorkflow.TaskQueue));

            await activities.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await handle.CancelAsync();

            var result = await handle.GetResultAsync();

            result.State.Should().Be(BuildState.Cancelled);
            activities.Transitions.Should().Contain(BuildState.Cancelled);
            activities.Calls.Should().Contain(nameof(FakeActivities.ReleaseRunnerAsync));
        });
    }

    /// <summary>The workflow reports its own state to a query.</summary>
    [Fact]
    public async Task The_workflow_answers_a_state_query()
    {
        var activities = new FakeActivities { BuildBlocks = true };

        using var worker = new TemporalWorker(
            temporal.Server.Client,
            new TemporalWorkerOptions(BuildWorkflow.TaskQueue)
                .AddAllActivities(activities)
                .AddWorkflow<BuildWorkflow>());

        await worker.ExecuteAsync(async () =>
        {
            var handle = await temporal.Server.Client.StartWorkflowAsync(
                (BuildWorkflow w) => w.RunAsync(Request),
                new WorkflowOptions(Guid.NewGuid().ToString(), BuildWorkflow.TaskQueue));

            await activities.BuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var state = await handle.QueryAsync((BuildWorkflow w) => w.CurrentState());
            state.Should().Be(BuildState.Building);

            await handle.CancelAsync();
            await handle.GetResultAsync();
        });
    }

    private async Task<BuildResult> RunAsync(FakeActivities activities)
    {
        using var worker = new TemporalWorker(
            temporal.Server.Client,
            new TemporalWorkerOptions(BuildWorkflow.TaskQueue)
                .AddAllActivities(activities)
                .AddWorkflow<BuildWorkflow>());

        return await worker.ExecuteAsync(async () =>
            await temporal.Server.Client.ExecuteWorkflowAsync(
                (BuildWorkflow w) => w.RunAsync(Request),
                new WorkflowOptions(Guid.NewGuid().ToString(), BuildWorkflow.TaskQueue)));
    }
}
