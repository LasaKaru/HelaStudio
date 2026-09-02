using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Auth;
using Shellwright.Api.Authorization;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;

namespace Shellwright.Api.Endpoints;

/// <summary>Mint an API token.</summary>
/// <param name="Name">Human-readable label.</param>
/// <param name="Role">Ceiling on what the token may do.</param>
/// <param name="WorkspaceId">Workspace to confine it to, or null for the organisation.</param>
public sealed record CreateApiTokenRequest(
    [param: Required, StringLength(120, MinimumLength = 1)] string Name,
    OrgRole Role = OrgRole.Developer,
    Guid? WorkspaceId = null);

/// <summary>A token as listed. Never contains the secret.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Name">Label.</param>
/// <param name="Prefix">Leading characters, enough to tell two tokens apart.</param>
/// <param name="Role">Ceiling on what it may do.</param>
/// <param name="WorkspaceId">Workspace it is confined to, if any.</param>
/// <param name="CreatedAt">When it was minted.</param>
/// <param name="LastUsedAt">Coarse last use.</param>
public sealed record ApiTokenResponse(
    Guid Id,
    string Name,
    string Prefix,
    string Role,
    Guid? WorkspaceId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>A token as returned once, at creation.</summary>
/// <param name="Token">⚠️ The only time the secret is ever available.</param>
/// <param name="Details">The stored record.</param>
public sealed record CreatedApiTokenResponse(string Token, ApiTokenResponse Details);

/// <summary>Managing the credentials CI and the command line use.</summary>
public static class ApiTokenEndpoints
{
    /// <summary>Maps the API token endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapApiTokenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/orgs/{orgId:guid}/tokens")
            .WithTags("API tokens")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapGet("/", ListAsync)
            .Produces<IReadOnlyList<ApiTokenResponse>>()
            .WithSummary("List an organisation's API tokens.");

        group.MapPost("/", CreateAsync)
            .Produces<CreatedApiTokenResponse>(StatusCodes.Status201Created)
            .WithSummary("Mint an API token and show the secret once.");

        group.MapDelete("/{tokenId:guid}", RevokeAsync)
            .WithSummary("Revoke an API token.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid orgId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var tokens = await database.ApiTokens
            .Where(x => x.OrgId == orgId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ApiTokenResponse(
                x.Id,
                x.Name,
                x.Prefix,
                x.Role.ToString(),
                x.WorkspaceId,
                x.CreatedAt,
                x.LastUsedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(tokens);
    }

    private static async Task<IResult> CreateAsync(
        Guid orgId,
        [FromBody] CreateApiTokenRequest request,
        ApiTokenService tokens,
        ShellwrightDbContext database,
        AccessGuard guard,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.CreateApiToken, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var caller = await guard.EffectiveRoleAsync(orgId, cancellationToken);

        // ⚠️ The ceiling that makes Permissions.CreateApiToken safe at Developer.
        // A token that could exceed its creator's role would be a privilege
        // escalation with a REST endpoint in front of it.
        if (request.Role > caller)
        {
            return ApiProblem.From(ApiErrors.Forbidden, "A token cannot be given a role above your own.");
        }

        if (request.WorkspaceId is { } workspaceId)
        {
            var workspaceOrg = await database.Workspaces
                .Where(x => x.Id == workspaceId)
                .Select(x => (Guid?)x.OrgId)
                .FirstOrDefaultAsync(cancellationToken);

            if (workspaceOrg != orgId)
            {
                return ApiProblem.From(ApiErrors.NotFound);
            }
        }

        var issued = await tokens.CreateAsync(
            orgId,
            request.WorkspaceId,
            request.Name,
            request.Role,
            guard.UserId!.Value,
            cancellationToken);

        await Audit.WriteAsync(
            database,
            new AuditEntry(
                orgId,
                guard.UserId,
                "api_token.created",
                "api_token",
                issued.Record.Id,
                new Dictionary<string, string> { ["role"] = request.Role.ToString() }),
            clock,
            cancellationToken);

        return TypedResults.Created(
            $"/v1/orgs/{orgId}/tokens/{issued.Record.Id}",
            new CreatedApiTokenResponse(
                issued.Token,
                new ApiTokenResponse(
                    issued.Record.Id,
                    issued.Record.Name,
                    issued.Record.Prefix,
                    issued.Record.Role.ToString(),
                    issued.Record.WorkspaceId,
                    issued.Record.CreatedAt,
                    null)));
    }

    private static async Task<IResult> RevokeAsync(
        Guid orgId,
        Guid tokenId,
        ShellwrightDbContext database,
        AccessGuard guard,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var token = await database.ApiTokens
            .Where(x => x.Id == tokenId && x.OrgId == orgId)
            .Select(x => new { x.Id, x.CreatedBy })
            .FirstOrDefaultAsync(cancellationToken);

        if (token is null)
        {
            return ApiProblem.From(ApiErrors.NotFound);
        }

        // Revoking your own token needs no special standing — it is the thing
        // to do the moment you suspect it has leaked, and requiring an admin
        // would put a delay in front of exactly that.
        if (token.CreatedBy != guard.UserId)
        {
            var access = await guard.ForOrgAsync(orgId, Permissions.RevokeOthersApiToken, cancellationToken);
            if (AccessGuard.Reject(access) is { } forbidden)
            {
                return forbidden;
            }
        }

        await database.ApiTokens
            .Where(x => x.Id == tokenId)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), cancellationToken);

        await Audit.WriteAsync(
            database,
            new AuditEntry(orgId, guard.UserId, "api_token.revoked", "api_token", tokenId),
            clock,
            cancellationToken);

        return TypedResults.NoContent();
    }
}
