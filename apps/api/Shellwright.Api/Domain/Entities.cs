using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Shellwright.Api.Domain;

/// <summary>A tenant. Everything billable, ownable, or isolatable hangs off an organisation.</summary>
public sealed class Org
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Display name, as the owner typed it.</summary>
    public required string Name { get; set; }

    /// <summary>URL-safe identifier, unique across live organisations.</summary>
    public required string Slug { get; set; }

    /// <summary>Billing plan.</summary>
    public OrgPlan Plan { get; set; } = OrgPlan.Free;

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Soft-delete marker. Non-null means the organisation is gone as far as the API is concerned.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Workspaces belonging to this organisation.</summary>
    public ICollection<Workspace> Workspaces { get; } = [];

    /// <summary>Memberships granting people access to this organisation.</summary>
    public ICollection<OrgMember> Members { get; } = [];
}

/// <summary>A person. Identity is global; authorisation is per organisation.</summary>
public sealed class User
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Login address, stored casefolded and unique.</summary>
    public required string Email { get; set; }

    /// <summary>Argon2id hash, or null for an account that only ever signed in through OAuth.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Set once the address has been proven.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Organisations this user belongs to.</summary>
    public ICollection<OrgMember> Memberships { get; } = [];
}

/// <summary>The join between a user and an organisation, carrying the role.</summary>
public sealed class OrgMember
{
    /// <summary>Organisation side of the membership.</summary>
    public Guid OrgId { get; set; }

    /// <summary>User side of the membership.</summary>
    public Guid UserId { get; set; }

    /// <summary>What this user may do in this organisation.</summary>
    public OrgRole Role { get; set; } = OrgRole.Viewer;

    /// <summary>When the membership was granted.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation to the organisation.</summary>
    public Org? Org { get; set; }

    /// <summary>Navigation to the user.</summary>
    public User? User { get; set; }
}

/// <summary>A grouping of apps inside an organisation.</summary>
public sealed class Workspace
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning organisation.</summary>
    public Guid OrgId { get; set; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>URL-safe identifier, unique within the organisation.</summary>
    public required string Slug { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Navigation to the owning organisation.</summary>
    public Org? Org { get; set; }

    /// <summary>Apps in this workspace.</summary>
    public ICollection<AppRecord> Apps { get; } = [];
}

/// <summary>
/// One shippable application.
/// </summary>
/// <remarks>
/// Named <c>AppRecord</c> rather than <c>App</c> because <c>App</c> collides
/// with too much in ASP.NET Core for the reader's comfort. The table is
/// <c>apps</c>.
/// </remarks>
public sealed class AppRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning workspace.</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Display name.</summary>
    public required string Name { get; set; }

    /// <summary>Reverse-DNS bundle identifier, shared by both platforms.</summary>
    public required string BundleId { get; set; }

    /// <summary>
    /// The version currently considered live.
    /// </summary>
    /// <remarks>
    /// This is the only mutable pointer into the append-only version history,
    /// and it is what makes "the current config" a single indexed lookup rather
    /// than an ordering query over every version ever saved.
    /// </remarks>
    public Guid? CurrentConfigVersionId { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the app is retired. Archived apps are readable but not writable.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Navigation to the owning workspace.</summary>
    public Workspace? Workspace { get; set; }

    /// <summary>Navigation to the current version.</summary>
    public ConfigVersion? CurrentConfigVersion { get; set; }
}

/// <summary>
/// An immutable, content-addressed snapshot of an app's configuration.
/// </summary>
/// <remarks>
/// ⚠️ Append-only. There is no code path that updates or deletes a row here,
/// and the application database role is not granted <c>UPDATE</c> or
/// <c>DELETE</c> on the table, so a future code path that tries will fail
/// loudly rather than quietly rewriting history the build cache depends on.
/// </remarks>
public sealed class ConfigVersion
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The app this version belongs to.</summary>
    public Guid AppId { get; set; }

    /// <summary>Schema version the body conforms to.</summary>
    public int SchemaVersion { get; set; }

    /// <summary>The resolved configuration, canonicalised before storage.</summary>
    [SuppressMessage(
        "Usage",
        "CA2227:Collection properties should be read only",
        Justification = "JsonObject is a document, not a collection the caller mutates in place. "
            + "EF Core materialises it by assignment, so the setter is required; making it read-only "
            + "would mean hand-writing a backing field and a constructor for no gain in safety.")]
    public required JsonObject Body { get; set; }

    /// <summary>Cache key covering everything that forces a native recompile.</summary>
    public required string CodeKey { get; set; }

    /// <summary>Cache key covering everything that needs only a resource repackage.</summary>
    public required string AssetKey { get; set; }

    /// <summary>Cache key covering everything that needs only a config patch and a re-sign.</summary>
    public required string ContentKey { get; set; }

    /// <summary>The user who saved it, or null if the author's account has since been deleted.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional free-text note, in the spirit of a commit message.</summary>
    public string? Message { get; set; }

    /// <summary>Navigation to the owning app.</summary>
    public AppRecord? App { get; set; }
}

/// <summary>A stored binary — an icon or splash image — addressed by its own hash.</summary>
public sealed class Asset
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning organisation. Assets are never shared across tenants, even when byte-identical.</summary>
    public Guid OrgId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the bytes. Unique per organisation.</summary>
    public required string Sha256 { get; set; }

    /// <summary>Media type as determined from the bytes, never from the upload header.</summary>
    public required string ContentType { get; set; }

    /// <summary>Size in bytes.</summary>
    public long Bytes { get; set; }

    /// <summary>Pixel width.</summary>
    public int Width { get; set; }

    /// <summary>Pixel height.</summary>
    public int Height { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>An append-only record of something a principal did.</summary>
public sealed class AuditEvent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Organisation the action happened in.</summary>
    public Guid OrgId { get; set; }

    /// <summary>User who did it, or null for a system action.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Dotted action name, such as <c>config.version.created</c>.</summary>
    public required string Action { get; set; }

    /// <summary>Type of the thing acted on, such as <c>app</c>.</summary>
    public required string SubjectType { get; set; }

    /// <summary>Identifier of the thing acted on.</summary>
    public required string SubjectId { get; set; }

    /// <summary>
    /// Structured detail.
    /// </summary>
    /// <remarks>
    /// ⚠️ Never a secret, and never a whole config body. This column is read by
    /// support staff and exported to customers; treat it as public within the
    /// organisation.
    /// </remarks>
    [SuppressMessage(
        "Usage",
        "CA2227:Collection properties should be read only",
        Justification = "JsonObject is a document, not a collection the caller mutates in place. "
            + "EF Core materialises it by assignment, so the setter is required; making it read-only "
            + "would mean hand-writing a backing field and a constructor for no gain in safety.")]
    public JsonObject? Meta { get; set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
