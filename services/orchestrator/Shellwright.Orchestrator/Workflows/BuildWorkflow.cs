using Shellwright.Orchestrator.Activities;
using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Shellwright.Orchestrator.Workflows;

/// <summary>
/// One build, from request to artifact.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Everything in this class is replayed. Temporal recovers a workflow by
/// re-executing its code against the recorded history, so anything that is not
/// a pure function of that history — the wall clock, a random number, a file, a
/// network call — produces a different decision on replay and the workflow
/// fails with a non-determinism error that is genuinely miserable to debug.
/// The rule is simple and absolute: side effects live in activities, never
/// here.
/// </para>
/// <para>
/// The compensation is the reason for the try/finally rather than a tidy
/// linear flow. A leased runner that is never released is a slot the whole
/// fleet has lost, and the paths that lose it are exactly the ones that do not
/// reach the end: a failure, a cancellation, a worker that died and came back.
/// </para>
/// </remarks>
[Workflow]
public class BuildWorkflow
{
    /// <summary>Task queue this workflow and its activities run on.</summary>
    public const string TaskQueue = "shellwright-builds";

    private static readonly ActivityOptions Quick = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumAttempts = 3,
            NonRetryableErrorTypes = BuildFailures.NonRetryable,
        },
    };

    private static readonly ActivityOptions Lease = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2,
            MaximumAttempts = 5,
            NonRetryableErrorTypes = BuildFailures.NonRetryable,
        },
    };

    private static readonly ActivityOptions Long = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(20),

        // ⚠️ A heartbeat timeout well under the start-to-close timeout is what
        // makes a dead runner detectable in a minute rather than in twenty. The
        // activity heartbeats every ten seconds; three missed beats is a
        // runner that is not coming back.
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(5),
            BackoffCoefficient = 2,
            MaximumAttempts = 3,
            NonRetryableErrorTypes = BuildFailures.NonRetryable,
        },

        // Cancellation must actually reach the activity, or a cancelled build
        // keeps a runner until its twenty minutes are up.
        CancellationType = ActivityCancellationType.WaitCancellationCompleted,
    };

    /// <summary>
    /// Options for the calls that must still run once the workflow is cancelled.
    /// </summary>
    /// <remarks>
    /// ⚠️ An explicit <see cref="System.Threading.CancellationToken.None"/>,
    /// because an activity inherits the workflow's cancellation token by
    /// default. On the cancellation path that means every call — including the
    /// one that releases the runner and the one that writes down that the build
    /// was cancelled — is itself cancelled the instant it starts. The lease
    /// then leaks, and the fleet loses a slot to every cancelled build.
    /// </remarks>
    private static readonly ActivityOptions Compensating = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(2),
        CancellationToken = CancellationToken.None,
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumAttempts = 3,
        },
    };

    private BuildState state = BuildState.Queued;

    /// <summary>Runs the build.</summary>
    /// <param name="request">What to build.</param>
    /// <returns>How it ended.</returns>
    [WorkflowRun]
    public async Task<BuildResult> RunAsync(BuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Cheap checks first, on the orchestrator. Leasing a runner for a
        // configuration that cannot build is the most expensive way to
        // discover a typo.
        var validation = await Workflow.ExecuteActivityAsync(
            (BuildActivities a) => a.ValidateAsync(request),
            Quick);

        if (!validation.IsValid)
        {
            await MoveAsync(request, BuildState.Failed);
            return BuildResult.Invalid(validation.Detail);
        }

        var cached = await Workflow.ExecuteActivityAsync(
            (BuildActivities a) => a.LookupCacheAsync(request, validation.Hashes),
            Quick);

        if (cached.Kind == CacheOutcome.Complete)
        {
            await MoveAsync(request, BuildState.Generating);
            await MoveAsync(request, BuildState.Building);
            await MoveAsync(request, BuildState.Verifying);
            await MoveAsync(request, BuildState.Succeeded);

            await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.RecordUsageAsync(
                    new UsageRecord(request.OrgId, request.BuildId, request.Platform, 0, true, cached.ArtifactBytes)),
                Quick);

            return BuildResult.FromCache(cached.ArtifactReference!);
        }

        var lease = await Workflow.ExecuteActivityAsync(
            (BuildActivities a) => a.LeaseRunnerAsync(request),
            Lease);

        try
        {
            await MoveAsync(request, BuildState.Generating);

            var generated = await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.GenerateAsync(request, lease, validation.Hashes),
                Quick);

            await MoveAsync(request, BuildState.Building);

            var built = await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.BuildAsync(request, lease, generated, cached),
                Long);

            await MoveAsync(request, BuildState.Verifying);

            await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.VerifyAsync(request, lease, built),
                Quick);

            var uploaded = await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.UploadAsync(request, lease, built, validation.Hashes),
                Quick);

            await MoveAsync(request, BuildState.Succeeded);

            await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.RecordUsageAsync(
                    new UsageRecord(
                        request.OrgId,
                        request.BuildId,
                        request.Platform,
                        built.RunnerSeconds,
                        built.WasPatched,
                        uploaded.Bytes)),
                Quick);

            return new BuildResult(
                BuildState.Succeeded,
                uploaded.ArtifactReference,
                built.WasPatched,
                built.RunnerSeconds,
                null);
        }
        catch (Exception exception) when (TemporalException.IsCanceledException(exception))
        {
            await MoveAsync(request, BuildState.Cancelled, Compensating);

            return new BuildResult(
                BuildState.Cancelled,
                null,
                false,
                0,
                new BuildFailure("BLD_CANCELLED", "The build was cancelled."));
        }
        catch (ActivityFailureException failure)
        {
            var cause = failure.InnerException as ApplicationFailureException;

            await MoveAsync(request, BuildState.Failed, Compensating);

            return new BuildResult(
                BuildState.Failed,
                null,
                false,
                0,
                new BuildFailure(cause?.ErrorType ?? "BLD_UNKNOWN", cause?.Message ?? failure.Message));
        }
        finally
        {
            // ⚠️ Always, and with the compensating options. A runner slot
            // leaked on the failure path is a slot the fleet never gets back,
            // and the failure path is exactly where an ordinary activity call
            // would itself be cancelled before it could release anything.
            await Workflow.ExecuteActivityAsync(
                (BuildActivities a) => a.ReleaseRunnerAsync(lease),
                Compensating);
        }
    }

    /// <summary>Reports where the build has got to, without touching the database.</summary>
    /// <returns>The current state.</returns>
    /// <remarks>
    /// A convenience for operators and for tests. The record customers read is
    /// the <c>builds</c> table, which the activities write — a query against a
    /// workflow is unavailable once its history has been archived.
    /// </remarks>
    [WorkflowQuery]
    public BuildState CurrentState() => state;

    private async Task MoveAsync(BuildRequest request, BuildState next, ActivityOptions? options = null)
    {
        // Checked here so an illegal move fails in the workflow, where the
        // history shows exactly which step attempted it, rather than inside an
        // activity that will be retried three times first.
        state = BuildStateMachine.Transition(state, next);

        await Workflow.ExecuteActivityAsync(
            (BuildActivities a) => a.RecordTransitionAsync(request.BuildId, next),
            options ?? Quick);
    }
}
