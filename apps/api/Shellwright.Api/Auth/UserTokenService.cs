using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Auth;

/// <summary>Issues and redeems the single-use tokens sent by email.</summary>
/// <param name="database">The database context.</param>
/// <param name="options">Authentication settings.</param>
/// <param name="clock">Time source.</param>
public sealed class UserTokenService(
    ShellwrightDbContext database,
    IOptions<AuthOptions> options,
    TimeProvider clock)
{
    private readonly AuthOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Issues a token for one purpose, invalidating any earlier one.</summary>
    /// <param name="userId">Whose account it acts on.</param>
    /// <param name="purpose">What presenting it authorises.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret to put in the emailed link.</returns>
    public async Task<string> IssueAsync(
        Guid userId,
        UserTokenPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        // ⚠️ Requesting a new reset link must invalidate the previous one.
        // Otherwise every link ever sent stays live for its full thirty
        // minutes, and "I requested three, in case" quietly widens the window.
        await database.UserTokens
            .Where(x => x.UserId == userId && x.Purpose == purpose && x.ConsumedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        var secret = TokenSecret.Create();

        database.UserTokens.Add(new UserToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = TokenSecret.Fingerprint(secret),
            CreatedAt = now,
            ExpiresAt = now + settings.EmailTokenLifetime,
        });

        await database.SaveChangesAsync(cancellationToken);
        return secret;
    }

    /// <summary>Redeems a token, exactly once.</summary>
    /// <param name="secret">The secret from the link.</param>
    /// <param name="purpose">The purpose the caller expects.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account the token belongs to, or null when it cannot be redeemed.</returns>
    /// <remarks>
    /// The purpose is matched rather than trusted: a verification token must not
    /// be redeemable as a password reset. They are the same shape, and without
    /// this check the weaker flow would be able to drive the stronger one.
    /// </remarks>
    public async Task<Guid?> RedeemAsync(
        string secret,
        UserTokenPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var fingerprint = TokenSecret.Fingerprint(secret);

        // Single statement, so two simultaneous redemptions cannot both win:
        // the ConsumedAt == null predicate is evaluated by the database as part
        // of the update, and exactly one of them affects a row.
        var affected = await database.UserTokens
            .Where(x => x.TokenHash == fingerprint
                && x.Purpose == purpose
                && x.ConsumedAt == null
                && x.ExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.ConsumedAt, now), cancellationToken);

        if (affected == 0)
        {
            return null;
        }

        return await database.UserTokens
            .Where(x => x.TokenHash == fingerprint)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
