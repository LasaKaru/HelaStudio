using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Shellwright.Api.Data;

namespace Shellwright.Api.Auth;

/// <summary>Authenticates <c>sw_live_…</c> credentials presented as a bearer token.</summary>
/// <param name="options">Scheme options.</param>
/// <param name="logger">Logger factory.</param>
/// <param name="encoder">URL encoder.</param>
/// <param name="tokens">Token resolution.</param>
/// <param name="tenant">Tenant scope for the rest of the request.</param>
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiTokenService tokens,
    TenantContext tenant)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var presented = header["Bearer ".Length..].Trim();
        var resolved = await tokens.ResolveAsync(presented, Context.RequestAborted);

        if (resolved is null)
        {
            // Deliberately not "unknown token" or "revoked token". The caller
            // learns only that it did not work; distinguishing the two would
            // let someone confirm a token exists.
            return AuthenticateResult.Fail("Invalid API token.");
        }

        // ⚠️ Stamped here, before any handler runs, because everything the
        // request touches afterwards is filtered by it. An API token acts as
        // the account that created it, so a token outlives its creator's
        // membership by exactly zero requests.
        tenant.UserId = resolved.UserId;
        await tokens.TouchAsync(resolved, Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, resolved.UserId.ToString()),
                new Claim(ShellwrightClaims.TokenId, resolved.TokenId.ToString()),
                new Claim(ShellwrightClaims.TokenOrg, resolved.OrgId.ToString()),
                new Claim(ShellwrightClaims.TokenRole, resolved.Role.ToString()),
                .. resolved.WorkspaceId is { } workspace
                    ? new[] { new Claim(ShellwrightClaims.TokenWorkspace, workspace.ToString()) }
                    : [],
            ],
            AuthSchemes.ApiToken,
            JwtRegisteredClaimNames.Sub,
            ClaimTypes.Role);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }
}
