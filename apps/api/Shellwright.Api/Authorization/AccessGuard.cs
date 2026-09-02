using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Shellwright.Api.Auth;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Authorization;

/// <summary>The outcome of an access check.</summary>
public enum Access
{
    /// <summary>
    /// The resource does not exist, or exists in a tenant the caller has no
    /// part in.
    /// </summary>
    /// <remarks>
    /// ⚠️ TC-S06-SEC-002: those two cases are deliberately one. Returning 403
    /// for the second would confirm that an identifier is real, which is enough
    /// to enumerate a competitor's app ids. The caller is told the same thing
    /// either way.
    /// </remarks>
    NotFound = 0,

    /// <summary>The caller can see the resource but may not do this to it.</summary>
    Forbidden = 1,

    /// <summary>Allowed.</summary>
    Granted = 2,
}

/// <summary>
/// Resource-based authorisation: checks the caller's standing in the tenant
/// that owns the thing being touched.
/// </summary>
/// <remarks>
/// ⚠️ Resource-based, never route-based. A route-level role requirement asks
/// "is this caller an admin", and an admin of one organisation satisfies it
/// while reaching into another. Every method here starts from the resource,
/// walks up to the organisation that owns it, and checks membership of *that*
/// organisation.
///
/// Row-level security does the first half of the work: a resource in another
/// tenant is not visible to the query at all, so it comes back as
/// <see cref="Access.NotFound"/> without any code deciding that it should. What
/// remains for this class is the role comparison, which the database
/// deliberately does not encode.
/// </remarks>
/// <param name="database">The database context, already scoped by the tenant interceptor.</param>
/// <param name="accessor">Access to the current request's principal.</param>
public sealed class AccessGuard(ShellwrightDbContext database, IHttpContextAccessor accessor)
{
    /// <summary>The authenticated user, or null when there is none.</summary>
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id
            : null;

    /// <summary>Checks access to an organisation.</summary>
    /// <param name="orgId">The organisation.</param>
    /// <param name="minimum">The least role that may do this.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<Access> ForOrgAsync(Guid orgId, OrgRole minimum, CancellationToken cancellationToken = default)
    {
        var exists = await database.Orgs.AnyAsync(x => x.Id == orgId && x.DeletedAt == null, cancellationToken);

        return exists
            ? await EvaluateAsync(orgId, workspaceId: null, minimum, cancellationToken)
            : Access.NotFound;
    }

    /// <summary>Checks access to a workspace.</summary>
    /// <param name="workspaceId">The workspace.</param>
    /// <param name="minimum">The least role that may do this.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<Access> ForWorkspaceAsync(
        Guid workspaceId,
        OrgRole minimum,
        CancellationToken cancellationToken = default)
    {
        var orgId = await database.Workspaces
            .Where(x => x.Id == workspaceId)
            .Select(x => (Guid?)x.OrgId)
            .FirstOrDefaultAsync(cancellationToken);

        return orgId is { } owner
            ? await EvaluateAsync(owner, workspaceId, minimum, cancellationToken)
            : Access.NotFound;
    }

    /// <summary>Checks access to an app.</summary>
    /// <param name="appId">The app.</param>
    /// <param name="minimum">The least role that may do this.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<Access> ForAppAsync(Guid appId, OrgRole minimum, CancellationToken cancellationToken = default)
    {
        var owner = await database.Apps
            .Where(x => x.Id == appId)
            .Select(x => new { x.WorkspaceId, OrgId = x.Workspace!.OrgId })
            .FirstOrDefaultAsync(cancellationToken);

        return owner is not null
            ? await EvaluateAsync(owner.OrgId, owner.WorkspaceId, minimum, cancellationToken)
            : Access.NotFound;
    }

    /// <summary>Turns a denial into the response it deserves.</summary>
    /// <param name="access">The outcome of a check.</param>
    /// <returns>A result, or null when access was granted.</returns>
    public static IResult? Reject(Access access) => access switch
    {
        Access.Granted => null,
        Access.Forbidden => TypedResults.Problem(
            title: "Not allowed",
            detail: "Your role in this organisation does not permit that.",
            statusCode: StatusCodes.Status403Forbidden),
        _ => TypedResults.Problem(
            title: "Not found",
            statusCode: StatusCodes.Status404NotFound),
    };

    /// <summary>The caller's effective role in an organisation, or null if they have none.</summary>
    /// <param name="orgId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The effective role.</returns>
    public async Task<OrgRole?> EffectiveRoleAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        if (UserId is not { } userId)
        {
            return null;
        }

        var membership = await database.OrgMembers
            .Where(x => x.OrgId == orgId && x.UserId == userId)
            .Select(x => (OrgRole?)x.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is not { } role)
        {
            return null;
        }

        // ⚠️ An API token narrows, and can never widen. The membership is the
        // authority; the token's own role is a ceiling laid over it. So a token
        // minted by an admin stops being an admin token the moment that person
        // is demoted, without anything having to go back and revoke it.
        if (TokenCeiling() is { } ceiling && ceiling < role)
        {
            role = ceiling;
        }

        return role;
    }

    private async Task<Access> EvaluateAsync(
        Guid orgId,
        Guid? workspaceId,
        OrgRole minimum,
        CancellationToken cancellationToken)
    {
        if (!TokenScopeAllows(orgId, workspaceId))
        {
            // A token confined to one workspace must not learn that another
            // exists, so this is Not Found rather than Forbidden.
            return Access.NotFound;
        }

        var role = await EffectiveRoleAsync(orgId, cancellationToken);

        return role is null
            ? Access.NotFound
            : role >= minimum ? Access.Granted : Access.Forbidden;
    }

    private OrgRole? TokenCeiling() =>
        Enum.TryParse<OrgRole>(Claim(ShellwrightClaims.TokenRole), out var role) ? role : null;

    private bool TokenScopeAllows(Guid orgId, Guid? workspaceId)
    {
        if (Claim(ShellwrightClaims.TokenOrg) is not { } tokenOrg)
        {
            // Not an API token: the session is not confined to one tenant.
            return true;
        }

        if (!Guid.TryParse(tokenOrg, out var scopedOrg) || scopedOrg != orgId)
        {
            return false;
        }

        if (Claim(ShellwrightClaims.TokenWorkspace) is not { } tokenWorkspace)
        {
            return true;
        }

        return Guid.TryParse(tokenWorkspace, out var scopedWorkspace)
            && workspaceId is { } target
            && scopedWorkspace == target;
    }

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}
