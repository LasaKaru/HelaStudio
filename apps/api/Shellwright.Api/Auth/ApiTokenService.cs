using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Auth;

/// <summary>What a presented API token resolved to.</summary>
/// <param name="TokenId">Identifier of the stored token.</param>
/// <param name="OrgId">Organisation it acts within.</param>
/// <param name="WorkspaceId">Workspace it is confined to, if any.</param>
/// <param name="Role">Ceiling on what it may do.</param>
/// <param name="UserId">The account it acts as.</param>
/// <param name="LastUsedAt">Previous use, so the stamp can be written coarsely.</param>
public sealed record ResolvedApiToken(
    Guid TokenId,
    Guid OrgId,
    Guid? WorkspaceId,
    OrgRole Role,
    Guid UserId,
    DateTimeOffset? LastUsedAt);

/// <summary>A newly minted token, returned once and never again.</summary>
/// <param name="Token">The full secret to show the creator.</param>
/// <param name="Record">The stored row.</param>
public sealed record IssuedApiToken(string Token, ApiToken Record);

/// <summary>Mints and resolves the long-lived credentials CI and the CLI use.</summary>
/// <param name="database">The database context.</param>
/// <param name="clock">Time source.</param>
public sealed class ApiTokenService(ShellwrightDbContext database, TimeProvider clock)
{
    /// <summary>Marks a live-environment token, so a leaked string is recognisable on sight.</summary>
    public const string LivePrefix = "sw_live_";

    /// <summary>How stale a last-used stamp may be before it is worth a write.</summary>
    /// <remarks>
    /// Updating on every request would put a write on the hot path of every CI
    /// build. The stamp exists to answer "is this token still in use", and
    /// five-minute resolution answers that just as well.
    /// </remarks>
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(5);

    /// <summary>Creates a token and returns the secret, which is not recoverable afterwards.</summary>
    /// <param name="orgId">Organisation the token acts within.</param>
    /// <param name="workspaceId">Workspace to confine it to, or null for the organisation.</param>
    /// <param name="name">Human-readable label.</param>
    /// <param name="role">Ceiling on what it may do.</param>
    /// <param name="createdBy">The account it acts as.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The secret and the stored row.</returns>
    public async Task<IssuedApiToken> CreateAsync(
        Guid orgId,
        Guid? workspaceId,
        string name,
        OrgRole role,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        var secret = LivePrefix + TokenSecret.Create();

        var record = new ApiToken
        {
            OrgId = orgId,
            WorkspaceId = workspaceId,
            Name = name,
            Prefix = secret[..(LivePrefix.Length + 6)],
            TokenHash = TokenSecret.Fingerprint(secret),
            Role = role,
            CreatedBy = createdBy,
            CreatedAt = clock.GetUtcNow(),
        };

        database.ApiTokens.Add(record);
        await database.SaveChangesAsync(cancellationToken);

        return new IssuedApiToken(secret, record);
    }

    /// <summary>Resolves a presented token to the principal it stands for.</summary>
    /// <param name="presented">The full token string from the Authorization header.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved principal, or null when the token is unknown or revoked.</returns>
    /// <remarks>
    /// Goes through <c>app_resolve_api_token</c>, which is the one query allowed
    /// to step outside the tenant policy — see the comment on the function.
    /// </remarks>
    public async Task<ResolvedApiToken?> ResolveAsync(string presented, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presented);

        if (!presented.StartsWith(LivePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var fingerprint = TokenSecret.Fingerprint(presented);

        var rows = await database.Database
            .SqlQuery<ApiTokenRow>(
                // ⚠️ Quoted aliases, not bare column names. SqlQuery maps by CLR
                // property name, and the function returns snake_case, so the
                // unaliased form compiles, deploys, and throws on the first
                // request that presents a token.
                $"""
                 SELECT id           AS "Id",
                        org_id       AS "OrgId",
                        workspace_id AS "WorkspaceId",
                        role         AS "Role",
                        created_by   AS "CreatedBy",
                        revoked_at   AS "RevokedAt",
                        last_used_at AS "LastUsedAt"
                 FROM app_resolve_api_token({fingerprint})
                 """)
            .ToListAsync(cancellationToken);

        if (rows.Count != 1 || rows[0].RevokedAt is not null)
        {
            return null;
        }

        var row = rows[0];
        return Enum.TryParse<OrgRole>(row.Role, out var role)
            ? new ResolvedApiToken(row.Id, row.OrgId, row.WorkspaceId, role, row.CreatedBy, row.LastUsedAt)
            : null;
    }

    /// <summary>Records that a token was used, at five-minute resolution.</summary>
    /// <param name="token">The resolved token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the stamp is written, or immediately when it is not needed.</returns>
    public async Task TouchAsync(ResolvedApiToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var now = clock.GetUtcNow();
        if (token.LastUsedAt is { } last && now - last < LastUsedResolution)
        {
            return;
        }

        await database.ApiTokens
            .Where(x => x.Id == token.TokenId)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.LastUsedAt, now), cancellationToken);
    }

    /// <summary>Shape returned by <c>app_resolve_api_token</c>.</summary>
    private sealed record ApiTokenRow(
        Guid Id,
        Guid OrgId,
        Guid? WorkspaceId,
        string Role,
        Guid CreatedBy,
        DateTimeOffset? RevokedAt,
        DateTimeOffset? LastUsedAt);
}
