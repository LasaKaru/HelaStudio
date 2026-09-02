using Microsoft.IdentityModel.JsonWebTokens;
using Shellwright.Api.Data;

namespace Shellwright.Api.Auth;

/// <summary>
/// Copies the authenticated subject into the tenant scope that database
/// connections are stamped with.
/// </summary>
/// <remarks>
/// ⚠️ Must run after authentication and before anything that opens a
/// connection. The API-token handler sets the scope itself, because it has to
/// resolve the credential before the subject is known; this middleware covers
/// the access-token path and is idempotent for the other.
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
public sealed class TenantScopeMiddleware(RequestDelegate next)
{
    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request.</param>
    /// <param name="tenant">The request's tenant scope.</param>
    /// <returns>A task for the rest of the pipeline.</returns>
    public async Task InvokeAsync(HttpContext context, TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenant);

        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (Guid.TryParse(subject, out var userId))
        {
            tenant.UserId = userId;
        }

        await next(context);
    }
}
