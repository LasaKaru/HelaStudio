using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Endpoints;

/// <summary>What an idempotency check decided.</summary>
/// <param name="Replay">The remembered response, when there is one.</param>
/// <param name="Conflict">True when the key was reused with a different body.</param>
/// <param name="Key">The key to record the outcome under, or null when none was sent.</param>
/// <param name="RequestHash">Fingerprint of the body just received.</param>
public sealed record IdempotencyCheck(
    IdempotencyRecord? Replay,
    bool Conflict,
    string? Key,
    string RequestHash);

/// <summary>Remembering and replaying the outcome of creating requests.</summary>
/// <param name="database">The database context.</param>
/// <param name="clock">Time source.</param>
public sealed class Idempotency(ShellwrightDbContext database, TimeProvider clock)
{
    /// <summary>Header clients send to make a creating request retryable.</summary>
    public const string HeaderName = "Idempotency-Key";

    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Looks for a remembered outcome for this request.</summary>
    /// <param name="request">The incoming request.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="body">The raw request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What to do.</returns>
    public async Task<IdempotencyCheck> CheckAsync(
        HttpRequest request,
        Guid userId,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = request.Headers[HeaderName].ToString();
        var hash = Fingerprint(body);

        if (string.IsNullOrWhiteSpace(key))
        {
            return new IdempotencyCheck(null, false, null, hash);
        }

        var endpoint = $"{request.Method} {request.Path}";
        var now = clock.GetUtcNow();

        var existing = await database.IdempotencyRecords
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.Endpoint == endpoint && x.Key == key,
                cancellationToken);

        if (existing is null || existing.ExpiresAt <= now)
        {
            return new IdempotencyCheck(null, false, key, hash);
        }

        // ⚠️ Same key, different body. This is not a retry — it is a client bug
        // or somebody probing, and replaying the first response would silently
        // discard whatever the second request was asking for.
        return string.Equals(existing.RequestHash, hash, StringComparison.Ordinal)
            ? new IdempotencyCheck(existing, false, key, hash)
            : new IdempotencyCheck(null, true, key, hash);
    }

    /// <summary>Remembers what a request returned, so a retry replays it.</summary>
    /// <param name="check">The check this request started with.</param>
    /// <param name="request">The incoming request.</param>
    /// <param name="userId">The caller.</param>
    /// <param name="statusCode">The status returned.</param>
    /// <param name="body">The response body, serialised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the outcome is recorded.</returns>
    public async Task RememberAsync(
        IdempotencyCheck check,
        HttpRequest request,
        Guid userId,
        int statusCode,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(request);

        if (check.Key is null)
        {
            return;
        }

        var now = clock.GetUtcNow();

        database.IdempotencyRecords.Add(new IdempotencyRecord
        {
            UserId = userId,
            Key = check.Key,
            Endpoint = $"{request.Method} {request.Path}",
            RequestHash = check.RequestHash,
            StatusCode = statusCode,
            ResponseBody = body,
            CreatedAt = now,
            ExpiresAt = now + Retention,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two retries arrived at once and both did the work. The response is
            // already correct; losing the race to record it changes nothing.
        }
    }

    private static string Fingerprint(string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
}
