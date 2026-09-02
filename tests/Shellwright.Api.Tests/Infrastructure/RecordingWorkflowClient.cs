using System.Collections.ObjectModel;
using Shellwright.Api.Builds;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Tests.Infrastructure;

/// <summary>
/// Records what the API asked Temporal to do, instead of doing it.
/// </summary>
/// <remarks>
/// ⚠️ A substitute here and a real Temporal in the orchestrator's tests, and
/// the split is on purpose. What the API decides is <i>whether</i> to start a
/// workflow — the required idempotency key, the per-organisation concurrency
/// limit, the authorisation check — and none of those become truer for having a
/// server behind them. Whether the workflow then runs correctly is the
/// orchestrator's responsibility, and its tests use a real Temporal because
/// what they check is Temporal's behaviour.
/// </remarks>
public sealed class RecordingWorkflowClient : IBuildWorkflowClient
{
    private readonly Lock gate = new();

    /// <summary>The builds a workflow was started for, in order.</summary>
    public Collection<Build> Started { get; } = [];

    /// <summary>The workflow ids cancellation was requested for, in order.</summary>
    public Collection<string> Cancelled { get; } = [];

    /// <summary>When set, StartAsync throws it. For testing the half-failed path.</summary>
    public Exception? StartFailure { get; set; }

    /// <inheritdoc />
    public Task StartAsync(Build build, CancellationToken cancellationToken = default)
    {
        if (StartFailure is not null)
        {
            return Task.FromException(StartFailure);
        }

        lock (gate)
        {
            Started.Add(build);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CancelAsync(string workflowId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            Cancelled.Add(workflowId);
        }

        return Task.CompletedTask;
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (gate)
        {
            Started.Clear();
            Cancelled.Clear();
            StartFailure = null;
        }
    }
}
