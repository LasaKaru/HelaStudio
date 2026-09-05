using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Runner;

/// <summary>Runner pool settings.</summary>
public sealed class RunnerPoolOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "RunnerPool";

    /// <summary>How many builds may hold a slot at once.</summary>
    [Range(1, 64)]
    public int Slots { get; set; } = 1;

    /// <summary>Where build workspaces are created.</summary>
    [Required]
    public string WorkspaceRoot { get; set; } = "/var/lib/shellwright/workspaces";

    /// <summary>
    /// Where per-app dependency caches live.
    /// </summary>
    /// <remarks>
    /// ⚠️ Per app, never shared between apps. A Gradle cache is a directory of
    /// resolved dependencies that a build can also write to, so one shared
    /// between tenants is a way for one customer's build to plant a jar that
    /// another customer's build compiles against.
    /// </remarks>
    [Required]
    public string CacheRoot { get; set; } = "/var/lib/shellwright/caches";

    /// <summary>
    /// How long a lease survives without renewal.
    /// </summary>
    /// <remarks>
    /// ⚠️ A time to live rather than an explicit release, so an orchestrator
    /// that dies mid-build frees its slot by simply stopping. A lease held
    /// until something explicitly frees it is a slot lost to every crash, and
    /// on a one-slot fleet that is the whole fleet.
    ///
    /// Comfortably longer than the activity heartbeat interval, so an ordinary
    /// scheduling hiccup does not look like a dead worker.
    /// </remarks>
    public TimeSpan LeaseTimeToLive { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Hands out slots on the machine the worker is running on.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Single-process, and honest about it. Two workers against this pool would
/// each hand out <see cref="RunnerPoolOptions.Slots"/> slots and between them
/// oversubscribe the host, which on a 12 GB box shared with Postgres means the
/// OOM killer picks a victim mid-build. A fleet across machines needs the lease
/// table in Postgres, which arrives with the build API; this is the
/// single-host implementation that keeps the seam real in the meantime.
/// </para>
/// <para>
/// ⚠️ Leases expire. Reclaiming happens on the next attempt to take a slot,
/// rather than on a timer, because a timer is another thing that can be dead
/// while the pool looks healthy.
/// </para>
/// </remarks>
/// <param name="options">Pool settings.</param>
/// <param name="clock">Where the pool reads the time from.</param>
/// <param name="logger">Where reclaimed leases are reported.</param>
public sealed class LocalRunnerPool(
    IOptions<RunnerPoolOptions> options,
    TimeProvider clock,
    ILogger<LocalRunnerPool> logger) : IRunnerPool
{
    private readonly RunnerPoolOptions settings =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    private readonly ConcurrentDictionary<string, Held> held = new(StringComparer.Ordinal);
    private readonly object gate = new();

    /// <summary>How many slots are taken right now.</summary>
    public int InUse
    {
        get
        {
            lock (gate)
            {
                ReclaimExpired();
                return held.Count;
            }
        }
    }

    /// <inheritdoc />
    public Task<Workflows.RunnerLease?> TryLeaseAsync(
        BuildRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            ReclaimExpired();

            if (held.Count >= settings.Slots)
            {
                return Task.FromResult<Workflows.RunnerLease?>(null);
            }

            var leaseId = Guid.NewGuid().ToString("N");

            var lease = new Workflows.RunnerLease(
                leaseId,
                RunnerId: Environment.MachineName,

                // Named by the lease, not by the build. A retried activity takes
                // a new lease and must get a workspace with nothing in it — a
                // directory named after the build would still hold whatever the
                // attempt that just failed left behind.
                WorkspaceRoot: Path.Combine(settings.WorkspaceRoot, leaseId),

                // Named by the app, because being reused is the entire point.
                CacheRoot: Path.Combine(settings.CacheRoot, request.AppId.ToString("N")));

            held[leaseId] = new Held(lease, clock.GetUtcNow() + settings.LeaseTimeToLive);

            return Task.FromResult<Workflows.RunnerLease?>(lease);
        }
    }

    /// <inheritdoc />
    public Task RenewAsync(Workflows.RunnerLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        lock (gate)
        {
            if (!held.TryGetValue(lease.LeaseId, out var existing))
            {
                // ⚠️ Loud. A renewal for a lease the pool does not hold means it
                // already expired and the slot may have been handed to another
                // build — so this worker is about to write into a workspace
                // somebody else owns.
                throw new InvalidOperationException(
                    $"Lease {lease.LeaseId} is not held. It expired and its slot has been reclaimed.");
            }

            held[lease.LeaseId] = existing with { ExpiresAt = clock.GetUtcNow() + settings.LeaseTimeToLive };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseAsync(Workflows.RunnerLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        lock (gate)
        {
            // Idempotent. Release runs on the compensation path, which Temporal
            // may run more than once, and a second release must not throw.
            held.TryRemove(lease.LeaseId, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Frees the slots of leases nobody renewed, and destroys their workspaces.
    /// </summary>
    /// <remarks>
    /// ⚠️ The workspace goes with the slot, and that ordering is not
    /// negotiable. A lease expires because the worker holding it stopped
    /// existing, which means nothing is ever going to call
    /// <c>ReleaseRunnerAsync</c> for it and nothing else will delete the
    /// directory. Handing the freed slot to the next tenant while the previous
    /// tenant's source is still on disk is the exact failure the isolation rule
    /// exists to prevent.
    /// </remarks>
    private void ReclaimExpired()
    {
        var now = clock.GetUtcNow();

        foreach (var (leaseId, entry) in held)
        {
            if (entry.ExpiresAt > now || !held.TryRemove(leaseId, out _))
            {
                continue;
            }

            logger.LogWarning(
                "Lease {LeaseId} on runner {RunnerId} expired without being renewed. Reclaiming its slot.",
                leaseId,
                entry.Lease.RunnerId);

            try
            {
                if (Directory.Exists(entry.Lease.WorkspaceRoot))
                {
                    Directory.Delete(entry.Lease.WorkspaceRoot, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // ⚠️ Reported at error level and the slot is NOT handed back.
                // Re-adding the lease keeps the slot out of circulation until
                // an operator clears the directory, because the alternative is
                // giving the next tenant a workspace full of somebody else's
                // source rather than merely running one build fewer.
                held[leaseId] = entry with { ExpiresAt = now + settings.LeaseTimeToLive };

                logger.LogError(
                    exception,
                    "Could not destroy the workspace at {WorkspaceRoot} for expired lease {LeaseId}. "
                    + "The slot is being withheld until it is removed.",
                    entry.Lease.WorkspaceRoot,
                    leaseId);
            }
        }
    }

    private sealed record Held(Workflows.RunnerLease Lease, DateTimeOffset ExpiresAt);
}
