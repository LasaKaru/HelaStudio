using System.Text.Json.Nodes;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Activities;

/// <summary>A configuration version, as the orchestrator needs it.</summary>
/// <param name="AppId">Which app.</param>
/// <param name="Body">The resolved document.</param>
/// <remarks>
/// ⚠️ No organisation here, deliberately. Who is charged travels on the
/// <see cref="BuildRequest"/>, set by the API when it created the build.
/// Carrying it here instead would mean joining apps to workspaces, and the
/// orchestrator's database role has no grant on workspaces — it has no business
/// enumerating a customer's organisation structure in order to compile a
/// project. An earlier version of this record did carry it, nothing ever read
/// it, and every configuration load failed with "permission denied".
/// </remarks>
public sealed record StoredConfig(Guid AppId, JsonObject Body);

/// <summary>Reads and writes the build record.</summary>
/// <remarks>
/// ⚠️ Postgres is the record of what happened; Temporal is what makes it
/// happen. Querying a workflow works until its history is archived, and it
/// cannot be joined to a tenant or paginated, so the customer-facing answer to
/// "how is my build going" comes from a table that activities write.
/// </remarks>
public interface IBuildStore
{
    /// <summary>Loads the exact configuration version a build was asked for.</summary>
    /// <param name="configVersionId">The version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or null when it does not exist.</returns>
    Task<StoredConfig?> LoadConfigAsync(Guid configVersionId, CancellationToken cancellationToken = default);

    /// <summary>Records that a build moved to a new state.</summary>
    /// <param name="buildId">The build.</param>
    /// <param name="state">Where it moved to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the transition is durable.</returns>
    /// <exception cref="IllegalBuildTransitionException">The move is not legal from the stored state.</exception>
    Task RecordTransitionAsync(Guid buildId, BuildState state, CancellationToken cancellationToken = default);

    /// <summary>Records a failure reason against a build.</summary>
    /// <param name="buildId">The build.</param>
    /// <param name="failure">Why it failed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the reason is durable.</returns>
    Task RecordFailureAsync(Guid buildId, BuildFailure failure, CancellationToken cancellationToken = default);

    /// <summary>Records the artifact a build produced.</summary>
    /// <param name="buildId">The build.</param>
    /// <param name="artifact">What it produced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the artifact is recorded.</returns>
    Task RecordArtifactAsync(
        Guid buildId,
        UploadedArtifact artifact,
        CancellationToken cancellationToken = default);

    /// <summary>Records metered usage.</summary>
    /// <param name="usage">What to charge for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is durable.</returns>
    Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken = default);
}

/// <summary>Finds and stores build artifacts by their cache keys.</summary>
public interface IArtifactCache
{
    /// <summary>Looks for a reusable artifact.</summary>
    /// <param name="appId">Scopes the lookup. ⚠️ Never shared across apps.</param>
    /// <param name="platform">Which platform's artifact.</param>
    /// <param name="type">
    /// Debug or release. ⚠️ Part of the key, not a filter applied afterwards: a
    /// debug-signed artifact satisfying a release build would hand a customer an
    /// unpublishable binary in answer to a request for a publishable one.
    /// </param>
    /// <param name="hashes">The three cache keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How much can be reused.</returns>
    Task<CacheLookup> LookupAsync(
        Guid appId,
        BuildPlatform platform,
        BuildType type,
        BuildHashes hashes,
        CancellationToken cancellationToken = default);

    /// <summary>Records an artifact against its cache keys.</summary>
    /// <param name="appId">Which app.</param>
    /// <param name="platform">Which platform.</param>
    /// <param name="type">Debug or release. Part of the key.</param>
    /// <param name="hashes">The three cache keys.</param>
    /// <param name="artifact">What was produced.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the entry is durable.</returns>
    Task StoreAsync(
        Guid appId,
        BuildPlatform platform,
        BuildType type,
        BuildHashes hashes,
        UploadedArtifact artifact,
        CancellationToken cancellationToken = default);
}

/// <summary>Hands out and reclaims runner slots.</summary>
public interface IRunnerPool
{
    /// <summary>Takes a slot, or reports that none is free.</summary>
    /// <param name="request">What the slot is for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lease, or null when the fleet is full.</returns>
    Task<RunnerLease?> TryLeaseAsync(BuildRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends a lease.
    /// </summary>
    /// <param name="lease">The lease.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the lease is extended.</returns>
    /// <remarks>
    /// ⚠️ Leases have a time to live and are renewed on heartbeat, so an
    /// orchestrator that dies mid-build releases its slot by simply stopping.
    /// A lease held until something explicitly frees it is a slot lost to every
    /// crash.
    /// </remarks>
    Task RenewAsync(RunnerLease lease, CancellationToken cancellationToken = default);

    /// <summary>Returns a slot to the fleet.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the slot is free.</returns>
    Task ReleaseAsync(RunnerLease lease, CancellationToken cancellationToken = default);
}
