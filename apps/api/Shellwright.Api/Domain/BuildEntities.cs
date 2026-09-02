using System.Diagnostics.CodeAnalysis;

namespace Shellwright.Api.Domain;

/// <summary>Which platform a build targets.</summary>
/// <remarks>
/// ⚠️ Declared here as well as in the orchestrator rather than shared. The two
/// services are deployed separately and versioned separately, and a shared
/// enum would make a rename in one a silent wire-format change in the other.
/// The numeric values are the contract, and <c>BuildContractTests</c> holds
/// them equal.
/// </remarks>
public enum BuildPlatform
{
    /// <summary>Android.</summary>
    Android = 0,

    /// <summary>iOS.</summary>
    Ios = 1,
}

/// <summary>What kind of artifact a build produces.</summary>
public enum BuildType
{
    /// <summary>Debug-signed, installable directly, not publishable.</summary>
    Debug = 0,

    /// <summary>Release-signed, for the store.</summary>
    Release = 1,
}

/// <summary>Where a build has got to.</summary>
/// <remarks>
/// ⚠️ Stored as the integer, and the values are permanent. A build row outlives
/// several deployments, and renumbering these would silently reinterpret every
/// historical row — including the ones a customer is billed against.
/// </remarks>
public enum BuildState
{
    /// <summary>Accepted, waiting for a runner.</summary>
    Queued = 0,

    /// <summary>Server-side validation is running.</summary>
    Validating = 1,

    /// <summary>The project is being generated.</summary>
    Generating = 2,

    /// <summary>The toolchain is running, or the cached artifact is being patched.</summary>
    Building = 3,

    /// <summary>The artifact is being checked.</summary>
    Verifying = 4,

    /// <summary>The artifact is being stored.</summary>
    Uploading = 5,

    /// <summary>Finished, with an artifact.</summary>
    Succeeded = 6,

    /// <summary>Finished, without one.</summary>
    Failed = 7,

    /// <summary>Stopped on request.</summary>
    Cancelled = 8,
}

/// <summary>How much of a previous build was reused.</summary>
public enum BuildCacheOutcome
{
    /// <summary>Nothing matched.</summary>
    Miss = 0,

    /// <summary>The code key matched: a full build against a warm dependency cache.</summary>
    Warm = 1,

    /// <summary>Code and assets matched: content was patched into the cached artifact.</summary>
    Patch = 2,

    /// <summary>Everything matched: the previous artifact was returned unchanged.</summary>
    Complete = 3,
}

/// <summary>
/// One build.
/// </summary>
/// <remarks>
/// ⚠️ Carries <see cref="OrgId"/> even though it is reachable through the app.
/// Metering is charged to an organisation and has to survive the app being
/// deleted, and a usage query that has to join four tables to find out who to
/// bill is a query somebody eventually writes wrongly.
/// </remarks>
public sealed class Build
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The app being built.</summary>
    public Guid AppId { get; set; }

    /// <summary>Who is charged.</summary>
    public Guid OrgId { get; set; }

    /// <summary>The exact configuration version being built.</summary>
    public Guid ConfigVersionId { get; set; }

    /// <summary>Which platform.</summary>
    public BuildPlatform Platform { get; set; }

    /// <summary>Debug or release.</summary>
    public BuildType Type { get; set; }

    /// <summary>Where it has got to.</summary>
    public BuildState State { get; set; } = BuildState.Queued;

    /// <summary>
    /// The Temporal workflow running it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Stored, because it is the only way to cancel. A build row with no
    /// workflow id is a build nobody can stop, and on a metered fleet an
    /// uncancellable build is money burning.
    /// </remarks>
    public required string WorkflowId { get; set; }

    /// <summary>A stable code for why it failed, or null.</summary>
    public string? FailureCode { get; set; }

    /// <summary>What a person can do about the failure, or null.</summary>
    public string? FailureMessage { get; set; }

    /// <summary>Content-addressed reference to the artifact, once there is one.</summary>
    public string? ArtifactReference { get; set; }

    /// <summary>Artifact size in bytes, once there is one.</summary>
    public long? ArtifactBytes { get; set; }

    /// <summary>How much of a previous build was reused.</summary>
    public BuildCacheOutcome CacheOutcome { get; set; } = BuildCacheOutcome.Miss;

    /// <summary>Metered runner time. Zero until the build has run.</summary>
    public int RunnerSeconds { get; set; }

    /// <summary>Who asked for it, or null once their account is deleted.</summary>
    public Guid? RequestedBy { get; set; }

    /// <summary>
    /// The idempotency key the request carried.
    /// </summary>
    /// <remarks>
    /// ⚠️ Unique per app, enforced by an index rather than by a read-then-write.
    /// Two identical requests racing each other both find nothing on a read and
    /// both start a build; a unique index makes the second one lose.
    /// </remarks>
    public required string IdempotencyKey { get; set; }

    /// <summary>When the build was accepted.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When a runner picked it up, or null.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it reached a terminal state, or null.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Navigation to the app.</summary>
    public AppRecord? App { get; set; }
}

/// <summary>
/// One state change, kept forever.
/// </summary>
/// <remarks>
/// ⚠️ Append-only, and the grant enforces it. The build row carries the current
/// state because that is what every query wants; this is the record of how it
/// got there, which is what every "why did this take eleven minutes" question
/// needs. A mutable history answers that question with whatever somebody
/// decided it should say.
/// </remarks>
public sealed class BuildTransition
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The build.</summary>
    public Guid BuildId { get; set; }

    /// <summary>The state it moved to.</summary>
    public BuildState State { get; set; }

    /// <summary>When.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation to the build.</summary>
    public Build? Build { get; set; }
}

/// <summary>
/// A reusable artifact, found by the three cache keys.
/// </summary>
/// <remarks>
/// ⚠️ Scoped to one app, never shared across them, and the unique index says
/// so. Two apps with byte-identical configurations still get separate rows:
/// sharing an artifact between tenants would mean one customer's binary being
/// handed to another, and the storage saved is not worth the sentence that
/// would have to be written about it afterwards.
/// </remarks>
public sealed class ArtifactCacheEntry
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The app this artifact belongs to.</summary>
    public Guid AppId { get; set; }

    /// <summary>Which platform it was built for.</summary>
    public BuildPlatform Platform { get; set; }

    /// <summary>Debug or release. A debug artifact must never satisfy a release build.</summary>
    public BuildType Type { get; set; }

    /// <summary>Everything that forces a native recompile.</summary>
    public required string CodeKey { get; set; }

    /// <summary>Everything that needs a resource repackage.</summary>
    public required string AssetKey { get; set; }

    /// <summary>Everything that needs only a content patch.</summary>
    public required string ContentKey { get; set; }

    /// <summary>Content-addressed reference to the artifact.</summary>
    public required string ArtifactReference { get; set; }

    /// <summary>Its size.</summary>
    public long ArtifactBytes { get; set; }

    /// <summary>When it was first stored.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When it was last served from.
    /// </summary>
    /// <remarks>
    /// The only mutable column here, and it exists so eviction can be by real
    /// use rather than by age. An artifact built once and served daily for a
    /// year should outlive one built and never touched again.
    /// </remarks>
    public DateTimeOffset LastUsedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation to the app.</summary>
    public AppRecord? App { get; set; }
}

/// <summary>
/// One metered build.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ One row per build, enforced by a unique index on <see cref="BuildId"/>.
/// The activity that writes this is retried by Temporal on any transient
/// failure, including one that happens after the row was committed — so
/// idempotence cannot be a read-then-write, and without the index a network
/// blip bills a customer twice.
/// </para>
/// <para>
/// ⚠️ Written from the runner path rather than by the API, so metering survives
/// an API outage. A build that ran and was never billed is a bug that only ever
/// fails in one direction.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "There is no Shellwright.Api.Usage namespace; the analyser matches an unrelated "
        + "framework namespace, and renaming the row that records usage to something else would make "
        + "the schema read worse to buy nothing.")]
public sealed class UsageRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Who is charged.</summary>
    public Guid OrgId { get; set; }

    /// <summary>Which build. Unique.</summary>
    public Guid BuildId { get; set; }

    /// <summary>What was built.</summary>
    public BuildPlatform Platform { get; set; }

    /// <summary>Metered runner time.</summary>
    public int RunnerSeconds { get; set; }

    /// <summary>Whether a compiler ran.</summary>
    public bool CacheHit { get; set; }

    /// <summary>Size of what was produced.</summary>
    public long ArtifactBytes { get; set; }

    /// <summary>When the build finished.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
