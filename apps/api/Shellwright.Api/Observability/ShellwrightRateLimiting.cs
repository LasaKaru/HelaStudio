using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using Shellwright.Api.Problems;

namespace Shellwright.Api.Observability;

/// <summary>Rate limit policy names.</summary>
public static class RateLimitPolicies
{
    /// <summary>Reads: generous, fixed window.</summary>
    public const string Read = "read";

    /// <summary>Writes: a token bucket, so a burst is allowed and a sustained flood is not.</summary>
    public const string Write = "write";

    /// <summary>Authentication: tight, and keyed by address as well as by caller.</summary>
    public const string Auth = "auth";
}

/// <summary>Registers rate limiting.</summary>
/// <remarks>
/// <para>
/// ⚠️ In-process, which means per instance. With one instance that is the whole
/// limit; with three it is three times the limit. That is an acceptable
/// approximation for protecting a host from a runaway client and an
/// unacceptable one for anything a customer is billed against, so the moment a
/// second instance exists this needs a shared store. Recorded in the sprint
/// review rather than left as a surprise.
/// </para>
/// <para>
/// Partitioned by authenticated subject where there is one, and by remote
/// address otherwise. Keying everything by address would put every customer
/// behind one corporate NAT into a single bucket.
/// </para>
/// </remarks>
public static class ShellwrightRateLimiting
{
    /// <summary>Adds the limiter and its policies.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Read, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(RateLimitPolicies.Write, context => RateLimitPartition.GetTokenBucketLimiter(
                PartitionKey(context),
                _ => new TokenBucketRateLimiterOptions
                {
                    // Enough burst for a studio autosaving while somebody
                    // types, refilled at a rate no human sustains.
                    TokenLimit = 60,
                    TokensPerPeriod = 30,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    // ⚠️ Tight, because this is the front door. The per-account
                    // backoff in IdentityService protects one account from
                    // guessing; this protects the host from somebody trying a
                    // thousand accounts once each, which the per-account
                    // counter cannot see.
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // ⚠️ Retry-After, always. A 429 without one tells a client to
                // back off by an amount it has to guess, and the guess is
                // usually "immediately".
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
                    ? window
                    : TimeSpan.FromMinutes(1);

                context.HttpContext.Response.Headers.RetryAfter =
                    ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                await ApiProblem
                    .From(ApiErrors.RateLimited, "Slow down and try again shortly.")
                    .ExecuteAsync(context.HttpContext);
            };
        });

        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "anonymous";
}
