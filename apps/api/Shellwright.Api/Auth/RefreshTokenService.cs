using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Auth;

/// <summary>Why a refresh attempt did not produce a new token.</summary>
public enum RefreshFailure
{
    /// <summary>No token in the store matches what was presented.</summary>
    Unknown = 0,

    /// <summary>The token exists but its family has been revoked.</summary>
    Revoked = 1,

    /// <summary>The token exists but has passed its absolute expiry.</summary>
    Expired = 2,

    /// <summary>
    /// The token exists and has already been exchanged once.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the theft signal. Handling it revokes the entire family, so
    /// the outcome is that both the attacker and the legitimate user are signed
    /// out — which is the point: the user notices and signs in again, and the
    /// stolen token is worthless.
    /// </remarks>
    Reused = 3,
}

/// <summary>A successful rotation.</summary>
/// <param name="Secret">The new refresh secret to send to the client.</param>
/// <param name="ExpiresAt">When the new token expires.</param>
/// <param name="UserId">Whose session it is.</param>
public sealed record RefreshResult(string Secret, DateTimeOffset ExpiresAt, Guid UserId);

/// <summary>
/// Issues, rotates, and revokes refresh tokens.
/// </summary>
/// <remarks>
/// Rotation on its own does not detect theft — an attacker who uses a copied
/// token first simply becomes the client, and the legitimate refresh then fails
/// with what looks like an ordinary expiry. Keeping the spent link and treating
/// its second presentation as an attack is what turns rotation into detection.
/// </remarks>
/// <param name="database">The database context.</param>
/// <param name="options">Authentication settings.</param>
/// <param name="clock">Time source.</param>
public sealed class RefreshTokenService(
    ShellwrightDbContext database,
    IOptions<AuthOptions> options,
    TimeProvider clock)
{
    private readonly AuthOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Starts a new rotation family for a fresh sign-in.</summary>
    /// <param name="userId">Who is signing in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret to hand to the client, and its expiry.</returns>
    public async Task<RefreshResult> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var secret = TokenSecret.Create();

        database.RefreshTokens.Add(new RefreshToken
        {
            FamilyId = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = TokenSecret.Fingerprint(secret),
            IssuedAt = now,
            ExpiresAt = now + settings.RefreshTokenLifetime,
        });

        await database.SaveChangesAsync(cancellationToken);
        return new RefreshResult(secret, now + settings.RefreshTokenLifetime, userId);
    }

    /// <summary>Exchanges a refresh secret for its successor.</summary>
    /// <param name="secret">The secret the client presented.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new token, or the reason none was issued.</returns>
    public async Task<(RefreshResult? Result, RefreshFailure Failure)> RotateAsync(
        string secret,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var fingerprint = TokenSecret.Fingerprint(secret);

        var presented = await database.RefreshTokens
            .AsTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == fingerprint, cancellationToken);

        if (presented is null)
        {
            return (null, RefreshFailure.Unknown);
        }

        if (presented.ConsumedAt is not null)
        {
            // Theft. Everything descended from this sign-in dies, including the
            // token the attacker is holding and the one the real user has.
            await RevokeFamilyAsync(presented.FamilyId, "refresh.reuse_detected", presented.UserId, cancellationToken);
            return (null, RefreshFailure.Reused);
        }

        if (presented.RevokedAt is not null)
        {
            return (null, RefreshFailure.Revoked);
        }

        if (presented.ExpiresAt <= now)
        {
            return (null, RefreshFailure.Expired);
        }

        var replacement = TokenSecret.Create();
        presented.ConsumedAt = now;

        database.RefreshTokens.Add(new RefreshToken
        {
            // ⚠️ The successor inherits the family and, deliberately, the
            // original expiry. Rotating must not extend a session
            // indefinitely — thirty days after signing in, you sign in again.
            FamilyId = presented.FamilyId,
            UserId = presented.UserId,
            TokenHash = TokenSecret.Fingerprint(replacement),
            IssuedAt = now,
            ExpiresAt = presented.ExpiresAt,
        });

        await database.SaveChangesAsync(cancellationToken);
        return (new RefreshResult(replacement, presented.ExpiresAt, presented.UserId), RefreshFailure.Unknown);
    }

    /// <summary>Revokes the family a secret belongs to, as an ordinary sign-out.</summary>
    /// <param name="secret">The secret the client presented.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether a family was found and revoked.</returns>
    public async Task<bool> SignOutAsync(string secret, CancellationToken cancellationToken = default)
    {
        var fingerprint = TokenSecret.Fingerprint(secret);

        var presented = await database.RefreshTokens
            .FirstOrDefaultAsync(x => x.TokenHash == fingerprint, cancellationToken);

        if (presented is null)
        {
            return false;
        }

        await RevokeFamilyAsync(presented.FamilyId, "refresh.signed_out", presented.UserId, cancellationToken);
        return true;
    }

    /// <summary>Revokes every live session a user has.</summary>
    /// <param name="userId">Whose sessions to end.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once every family is revoked.</returns>
    /// <remarks>
    /// Used after a password change. The usual reason to change a password is
    /// the belief that somebody else has it, so leaving that somebody's refresh
    /// token live would make the change theatre.
    /// </remarks>
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        var revoked = await database.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, now), cancellationToken);

        if (revoked == 0)
        {
            return;
        }

        database.SecurityEvents.Add(new SecurityEvent
        {
            Kind = "refresh.all_revoked",
            UserId = userId,
            Detail = $"sessions={revoked}",
            At = now,
        });

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        string kind,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await database.RefreshTokens
            .Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, now), cancellationToken);

        database.SecurityEvents.Add(new SecurityEvent
        {
            Kind = kind,
            UserId = userId,

            // ⚠️ The family id, never the token or its hash. A security log that
            // contains credentials is a credential store with worse access
            // control than the one it describes.
            Detail = $"family={familyId}",
            At = now,
        });

        await database.SaveChangesAsync(cancellationToken);
    }
}
