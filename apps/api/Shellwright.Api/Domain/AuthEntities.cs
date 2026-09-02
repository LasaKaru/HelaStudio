namespace Shellwright.Api.Domain;

/// <summary>What a single-use emailed token is for.</summary>
public enum UserTokenPurpose
{
    /// <summary>Proves the address on a new account.</summary>
    EmailVerification = 0,

    /// <summary>Authorises setting a new password without knowing the old one.</summary>
    PasswordReset = 1,
}

/// <summary>
/// One link in a refresh-token rotation family.
/// </summary>
/// <remarks>
/// ⚠️ Rows are never deleted while their family is live, and that is the point.
/// Rotation alone does not detect theft: an attacker who copies a refresh token
/// and uses it before the legitimate client simply becomes the client. Keeping
/// the spent link lets the second presentation be recognised as a replay, at
/// which point the whole family is revoked and both parties are logged out.
/// The legitimate user notices; that is the desired outcome.
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Groups every token descended from one sign-in.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>Whose session this is.</summary>
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the secret, hex. The secret itself is never stored.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Issue time.</summary>
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Absolute expiry, independent of use.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when this token was exchanged for its successor.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Set when the family was revoked, whether by logout or by reuse detection.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>A long-lived credential for CI and the command line.</summary>
public sealed class ApiToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Organisation the token acts within.</summary>
    public Guid OrgId { get; set; }

    /// <summary>Workspace the token is confined to, or null for the whole organisation.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>Human-readable label chosen at creation.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The leading characters of the secret, kept so the owner can tell two
    /// tokens apart in a list.
    /// </summary>
    /// <remarks>
    /// Deliberately short. It is an identifier, not a fragment of the
    /// credential — long enough to recognise, far too short to narrow a
    /// brute-force search of a 256-bit secret.
    /// </remarks>
    public required string Prefix { get; set; }

    /// <summary>SHA-256 of the secret, hex.</summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// The ceiling on what this token may do.
    /// </summary>
    /// <remarks>
    /// ⚠️ A ceiling, not a grant. The effective role is the lesser of this and
    /// the creating user's own membership, so a developer cannot mint an admin
    /// token and an admin's token stops working the moment they are demoted.
    /// </remarks>
    public OrgRole Role { get; set; } = OrgRole.Developer;

    /// <summary>Who created it.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Coarse last-used timestamp, for spotting tokens nobody needs any more.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Set when the token is withdrawn.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>A single-use token delivered by email.</summary>
public sealed class UserToken
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Whose account it acts on.</summary>
    public Guid UserId { get; set; }

    /// <summary>What presenting it authorises.</summary>
    public UserTokenPurpose Purpose { get; set; }

    /// <summary>SHA-256 of the secret, hex.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Expiry, thirty minutes after issue.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set the first time it is redeemed; a second redemption fails.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}

/// <summary>A link between an account here and an account at an identity provider.</summary>
public sealed class OAuthIdentity
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Provider key, such as <c>github</c> or <c>google</c>.</summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The provider's stable identifier for the account.
    /// </summary>
    /// <remarks>
    /// ⚠️ The numeric or opaque id, never the email address or the username.
    /// Both of those can be changed by their owner and reassigned to somebody
    /// else, which would hand a stranger the account.
    /// </remarks>
    public required string ProviderUserId { get; set; }

    /// <summary>The local account.</summary>
    public Guid UserId { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An append-only record of an authentication-relevant event.
/// </summary>
/// <remarks>
/// Separate from <see cref="AuditEvent"/> for two reasons. Audit events belong
/// to an organisation and are shown to customers; these do not and are not — a
/// failed sign-in has no tenant. And the application role is granted
/// <c>INSERT</c> here and nothing else, so the API can write its own security
/// log but cannot read, alter, or erase it.
/// </remarks>
public sealed class SecurityEvent
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Dotted event name, such as <c>refresh.reuse_detected</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>The account involved, when one is known.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Free-text detail. ⚠️ Never a token, a hash, or a password.</summary>
    public string? Detail { get; set; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}
