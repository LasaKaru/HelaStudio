using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Auth;

/// <summary>How a sign-in attempt ended.</summary>
public enum SignInOutcome
{
    /// <summary>The address is unknown, or the password is wrong. Deliberately one case.</summary>
    InvalidCredentials = 0,

    /// <summary>Too many recent failures; the account is backing off.</summary>
    LockedOut = 1,

    /// <summary>Signed in.</summary>
    Success = 2,
}

/// <summary>Accounts: creation, sign-in, and the failure counters that guard it.</summary>
/// <param name="database">The database context.</param>
/// <param name="hasher">Password hashing.</param>
/// <param name="options">Authentication settings.</param>
/// <param name="clock">Time source.</param>
public sealed class IdentityService(
    ShellwrightDbContext database,
    PasswordHasher hasher,
    IOptions<AuthOptions> options,
    TimeProvider clock)
{
    private readonly AuthOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Casefolds an address so that one person cannot hold two accounts that
    /// look identical in every client that will ever display them.
    /// </summary>
    /// <param name="email">The address as typed.</param>
    /// <returns>The normalised form used for storage and lookup.</returns>
    /// <remarks>
    /// RFC 5321 makes the local part case-sensitive, and essentially no mail
    /// provider honours that. Treating <c>Ada@example.com</c> and
    /// <c>ada@example.com</c> as different accounts would be technically
    /// defensible and would produce a stream of "I can't log in" reports and a
    /// straightforward account-confusion attack.
    /// </remarks>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "The rule guards against the Turkish dotless-i round trip in security comparisons. "
            + "This value is also what gets displayed back to the user and printed in emails, and an address "
            + "shown in capitals reads as a mistake. Addresses are restricted to ASCII by the schema, so the "
            + "case the rule warns about cannot arise here.")]
    public static string NormaliseEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    /// <summary>Creates an account.</summary>
    /// <param name="email">Address, normalised before storage.</param>
    /// <param name="password">Plaintext password, hashed before storage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new user, or null when the address is already taken.</returns>
    public async Task<User?> RegisterAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var normalised = NormaliseEmail(email);

        if (await database.Users.AnyAsync(x => x.Email == normalised, cancellationToken))
        {
            return null;
        }

        var user = new User
        {
            Email = normalised,
            PasswordHash = hasher.Hash(password),
            CreatedAt = clock.GetUtcNow(),
        };

        database.Users.Add(user);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two simultaneous registrations for the same address: the unique
            // index decides, and the loser reports the same "already taken" as
            // the sequential case rather than a 500. Anything else is a real
            // failure and is rethrown.
            database.Entry(user).State = EntityState.Detached;

            if (await database.Users.AnyAsync(x => x.Email == normalised, cancellationToken))
            {
                return null;
            }

            throw;
        }

        return user;
    }

    /// <summary>Checks a password and applies the failure counters.</summary>
    /// <param name="email">Address as typed.</param>
    /// <param name="password">Plaintext password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome, and the user when it succeeded.</returns>
    public async Task<(SignInOutcome Outcome, User? User)> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalised = NormaliseEmail(email);
        var now = clock.GetUtcNow();

        var user = await database.Users
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Email == normalised, cancellationToken);

        if (user?.PasswordHash is null)
        {
            // ⚠️ Still spend the time. An unknown address that answers in a
            // millisecond while a known one takes a hundred turns this endpoint
            // into an account-enumeration oracle that rate limiting cannot fix,
            // because one request already answers the question.
            hasher.VerifyDecoy(password);
            return (SignInOutcome.InvalidCredentials, null);
        }

        if (user.LockedUntil is { } until && until > now)
        {
            hasher.VerifyDecoy(password);
            return (SignInOutcome.LockedOut, null);
        }

        var verification = hasher.Verify(password, user.PasswordHash);

        if (verification == PasswordVerification.Failed)
        {
            user.FailedLoginCount++;
            user.LockedUntil = LockoutUntil(user.FailedLoginCount, now);
            await database.SaveChangesAsync(cancellationToken);
            return (SignInOutcome.InvalidCredentials, null);
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            // The password is correct and the stored hash predates a parameter
            // increase. This is the only moment the plaintext is available, so
            // it is the only moment the upgrade can happen.
            user.PasswordHash = hasher.Hash(password);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await database.SaveChangesAsync(cancellationToken);

        return (SignInOutcome.Success, user);
    }

    /// <summary>Replaces a password and clears any backoff.</summary>
    /// <param name="user">The account, tracked by the caller's context.</param>
    /// <param name="password">The new plaintext password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the change is saved.</returns>
    public async Task SetPasswordAsync(User user, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.PasswordHash = hasher.Hash(password);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Works out how long an account should refuse attempts after a failure.
    /// </summary>
    /// <remarks>
    /// Exponential from the threshold, capped. The cap matters more than the
    /// growth: an uncapped backoff is a denial-of-service anyone can aim at any
    /// account whose address they know, by failing to log in as them.
    /// </remarks>
    private DateTimeOffset? LockoutUntil(int failures, DateTimeOffset now)
    {
        if (failures < settings.LockoutThreshold)
        {
            return null;
        }

        var steps = Math.Min(failures - settings.LockoutThreshold, 16);
        var delay = settings.LockoutBaseDelay * Math.Pow(2, steps);

        return now + (delay < settings.LockoutMaxDelay ? delay : settings.LockoutMaxDelay);
    }
}
