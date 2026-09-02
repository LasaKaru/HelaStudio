namespace Shellwright.Orchestrator.Workflows;

/// <summary>The three cache keys, as the workflow carries them.</summary>
/// <param name="CodeKey">Changes here force a full native recompile.</param>
/// <param name="AssetKey">Changes here need only a resource repackage.</param>
/// <param name="ContentKey">Changes here need only a config patch and a re-sign.</param>
public sealed record BuildHashes(string CodeKey, string AssetKey, string ContentKey);

/// <summary>What server-side validation found.</summary>
/// <param name="IsValid">Whether the build may proceed.</param>
/// <param name="Detail">What is wrong, when something is.</param>
/// <param name="Hashes">The cache keys, computed from the resolved document.</param>
public sealed record ValidationOutcome(bool IsValid, string Detail, BuildHashes Hashes);

/// <summary>How much of a previous build can be reused.</summary>
public enum CacheOutcome
{
    /// <summary>Nothing matched. A full build.</summary>
    Miss = 0,

    /// <summary>
    /// The code key matched but assets or content did not.
    /// </summary>
    /// <remarks>
    /// ⚠️ The unit-economics case, and the reason the cache key is split three
    /// ways at all. Nothing that affects compiled code has changed, so the
    /// cached artifact can have its resources replaced and be re-signed in
    /// under a minute instead of recompiled in four. Most user-triggered builds
    /// land here: people change a colour, a label, a start page.
    /// </remarks>
    Patchable = 1,

    /// <summary>All three matched. The previous artifact is the answer.</summary>
    Complete = 2,
}

/// <summary>What the cache lookup found.</summary>
/// <param name="Kind">How much can be reused.</param>
/// <param name="ArtifactReference">The cached artifact, when there is one.</param>
/// <param name="ArtifactBytes">Its size, for metering.</param>
public sealed record CacheLookup(CacheOutcome Kind, string? ArtifactReference, long ArtifactBytes)
{
    /// <summary>Nothing to reuse.</summary>
    public static CacheLookup Miss { get; } = new(CacheOutcome.Miss, null, 0);
}

/// <summary>A leased runner slot.</summary>
/// <param name="LeaseId">Identifies the lease, for renewal and release.</param>
/// <param name="RunnerId">Which runner was leased.</param>
/// <param name="WorkspaceRoot">Where this build's files live. The only writable location.</param>
/// <param name="CacheRoot">Where this app's dependency cache lives. ⚠️ Per app, never shared.</param>
/// <remarks>
/// ⚠️ The paths live on the lease rather than in a handle held by the sandbox,
/// because activities are separate invocations that may run on different
/// workers. Temporal persists this record in the workflow history, so every
/// activity reconstructs the same workspace from it — a sandbox that remembered
/// "the current build" instead would be one instance away from serving two at
/// once and mixing their files.
/// </remarks>
public sealed record RunnerLease(
    string LeaseId,
    string RunnerId,
    string WorkspaceRoot,
    string CacheRoot);

/// <summary>What generation produced.</summary>
/// <param name="ProjectRoot">Where the generated project was written.</param>
/// <param name="FileCount">How many files, for the log and for a sanity check.</param>
public sealed record GeneratedProject(string ProjectRoot, int FileCount);

/// <summary>What the toolchain produced.</summary>
/// <param name="ArtifactPath">Path to the artifact on the runner.</param>
/// <param name="RunnerSeconds">Metered runner time.</param>
/// <param name="WasPatched">True when the resource-patch path was taken rather than a full build.</param>
public sealed record BuiltArtifact(string ArtifactPath, int RunnerSeconds, bool WasPatched);

/// <summary>Where an artifact ended up.</summary>
/// <param name="ArtifactReference">Content-addressed reference.</param>
/// <param name="Bytes">Size.</param>
public sealed record UploadedArtifact(string ArtifactReference, long Bytes);

/// <summary>One metered build.</summary>
/// <param name="OrgId">Who is charged.</param>
/// <param name="BuildId">Which build.</param>
/// <param name="Platform">What was built.</param>
/// <param name="RunnerSeconds">Metered runner time. Zero on a cache hit.</param>
/// <param name="CacheHit">Whether a compiler ran.</param>
/// <param name="ArtifactBytes">Size of what was produced.</param>
/// <remarks>
/// ⚠️ Written from the runner path rather than by the API, so that metering
/// survives an API outage. A build that ran and was never billed is a bug that
/// only ever fails in one direction.
/// </remarks>
public sealed record UsageRecord(
    Guid OrgId,
    Guid BuildId,
    BuildPlatform Platform,
    int RunnerSeconds,
    bool CacheHit,
    long ArtifactBytes);
