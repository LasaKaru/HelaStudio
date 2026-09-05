using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Authorization;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;

namespace Shellwright.Api.Endpoints;

/// <summary>Create an organisation.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL-safe identifier, generated from the name when omitted.</param>
public sealed record CreateOrgRequest(
    [param: Required, StringLength(120, MinimumLength = 1)] string Name,
    [param: StringLength(64)] string? Slug);

/// <summary>Create a workspace.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL-safe identifier, generated from the name when omitted.</param>
public sealed record CreateWorkspaceRequest(
    [param: Required, StringLength(120, MinimumLength = 1)] string Name,
    [param: StringLength(64)] string? Slug);

/// <summary>Change somebody's role.</summary>
/// <param name="Role">The role to grant.</param>
public sealed record SetMemberRoleRequest([param: Required] OrgRole Role);

/// <summary>An organisation as the API reports it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL-safe identifier.</param>
/// <param name="Plan">Billing plan.</param>
/// <param name="Role">The caller's effective role.</param>
public sealed record OrgResponse(Guid Id, string Name, string Slug, string Plan, string Role);

/// <summary>A workspace as the API reports it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="OrgId">Owning organisation.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">URL-safe identifier.</param>
public sealed record WorkspaceResponse(Guid Id, Guid OrgId, string Name, string Slug);

/// <summary>A membership as the API reports it.</summary>
/// <param name="UserId">The member.</param>
/// <param name="Email">Their address.</param>
/// <param name="Role">Their role.</param>
public sealed record MemberResponse(Guid UserId, string Email, string Role);

/// <summary>Organisations, workspaces, and membership.</summary>
public static class OrgEndpoints
{
    /// <summary>Maps the organisation and workspace endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapOrgEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1")
            .WithTags("Organisations")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapGet("/orgs", ListOrgsAsync)
            .Produces<IReadOnlyList<OrgResponse>>()
            .WithSummary("List the organisations the caller belongs to.");

        group.MapPost("/orgs", CreateOrgAsync)
            .Produces<OrgResponse>(StatusCodes.Status201Created)
            .WithSummary("Create an organisation and become its owner.");

        group.MapGet("/orgs/{orgId:guid}/workspaces", ListWorkspacesAsync)
            .Produces<IReadOnlyList<WorkspaceResponse>>()
            .WithSummary("List an organisation's workspaces.");

        group.MapPost("/orgs/{orgId:guid}/workspaces", CreateWorkspaceAsync)
            .Produces<WorkspaceResponse>(StatusCodes.Status201Created)
            .WithSummary("Create a workspace.");

        group.MapGet("/orgs/{orgId:guid}/members", ListMembersAsync)
            .Produces<IReadOnlyList<MemberResponse>>()
            .WithSummary("List an organisation's members.");

        group.MapPut("/orgs/{orgId:guid}/members/{userId:guid}", SetMemberRoleAsync)
            .WithSummary("Grant or change a member's role.");

        return app;
    }

    private static async Task<IResult> ListOrgsAsync(
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        // No WHERE on membership, and that is not an oversight: the tenant
        // policy has already reduced this table to the caller's organisations.
        // Writing the filter again would be harmless and would also be the
        // reason nobody notices when the policy stops working.
        var orgs = await database.Orgs
            .Where(x => x.DeletedAt == null)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Slug, x.Plan })
            .ToListAsync(cancellationToken);

        var responses = new List<OrgResponse>(orgs.Count);
        foreach (var org in orgs)
        {
            var role = await guard.EffectiveRoleAsync(org.Id, cancellationToken);
            responses.Add(new OrgResponse(
                org.Id,
                org.Name,
                org.Slug,
                org.Plan.ToString(),
                (role ?? OrgRole.Viewer).ToString()));
        }

        return TypedResults.Ok(responses);
    }

    private static async Task<IResult> CreateOrgAsync(
        [FromBody] CreateOrgRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (guard.UserId is not { } userId)
        {
            return ApiProblem.From(ApiErrors.Unauthenticated);
        }

        var slug = Slug.From(request.Slug ?? request.Name);

        if (slug.Length == 0)
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["slug"] = ["A name must contain at least one letter or digit."],
            });
        }

        var org = new Org
        {
            Name = request.Name,
            Slug = slug,
            CreatedAt = clock.GetUtcNow(),
        };

        // ⚠️ One transaction, and the order matters. The organisation is
        // invisible to its own creator until the membership row exists — the
        // policy that makes that true is the same one that stops anybody else
        // claiming it. Committing the two separately would leave an
        // unclaimable organisation behind on any failure in between.
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            database.Orgs.Add(org);
            database.OrgMembers.Add(new OrgMember
            {
                OrgId = org.Id,
                UserId = userId,
                Role = OrgRole.Owner,
                CreatedAt = clock.GetUtcNow(),
            });

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsUniqueViolation())
            {
                return ApiProblem.From(ApiErrors.NameTaken, $"'{slug}' is already in use. Choose another.");
            }

            await Audit.WriteAsync(
                database,
                new AuditEntry(org.Id, userId, "org.created", "org", org.Id),
                clock,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Created(
            $"/v1/orgs/{org.Id}",
            new OrgResponse(org.Id, org.Name, org.Slug, org.Plan.ToString(), OrgRole.Owner.ToString()));
    }

    private static async Task<IResult> ListWorkspacesAsync(
        Guid orgId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var workspaces = await database.Workspaces
            .Where(x => x.OrgId == orgId)
            .OrderBy(x => x.Name)
            .Select(x => new WorkspaceResponse(x.Id, x.OrgId, x.Name, x.Slug))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(workspaces);
    }

    private static async Task<IResult> CreateWorkspaceAsync(
        Guid orgId,
        [FromBody] CreateWorkspaceRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.CreateWorkspace, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var slug = Slug.From(request.Slug ?? request.Name);

        if (slug.Length == 0)
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["slug"] = ["A name must contain at least one letter or digit."],
            });
        }

        var workspace = new Workspace
        {
            OrgId = orgId,
            Name = request.Name,
            Slug = slug,
            CreatedAt = clock.GetUtcNow(),
        };

        database.Workspaces.Add(workspace);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return ApiProblem.From(
                ApiErrors.NameTaken,
                $"'{slug}' is already a workspace in this organisation.");
        }

        return TypedResults.Created(
            $"/v1/workspaces/{workspace.Id}",
            new WorkspaceResponse(workspace.Id, workspace.OrgId, workspace.Name, workspace.Slug));
    }

    private static async Task<IResult> ListMembersAsync(
        Guid orgId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var members = await database.OrgMembers
            .Where(x => x.OrgId == orgId)
            .OrderBy(x => x.User!.Email)
            .Select(x => new MemberResponse(x.UserId, x.User!.Email, x.Role.ToString()))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(members);
    }

    private static async Task<IResult> SetMemberRoleAsync(
        Guid orgId,
        Guid userId,
        [FromBody] SetMemberRoleRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForOrgAsync(orgId, Permissions.ManageMembers, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var caller = await guard.EffectiveRoleAsync(orgId, cancellationToken);

        // ⚠️ An admin cannot mint an owner. Otherwise the highest role in the
        // organisation is available to the second-highest, and the distinction
        // between them is decorative.
        if (request.Role > caller)
        {
            return ApiProblem.From(ApiErrors.Forbidden, "You cannot grant a role above your own.");
        }

        var existing = await database.OrgMembers
            .AsTracking()
            .FirstOrDefaultAsync(x => x.OrgId == orgId && x.UserId == userId, cancellationToken);

        if (existing is null)
        {
            // The user must already exist; inviting somebody who has no account
            // is a flow of its own, and it is not this one.
            if (!await database.Users.AnyAsync(x => x.Id == userId, cancellationToken))
            {
                return ApiProblem.From(ApiErrors.NotFound);
            }

            database.OrgMembers.Add(new OrgMember
            {
                OrgId = orgId,
                UserId = userId,
                Role = request.Role,
                CreatedAt = clock.GetUtcNow(),
            });
        }
        else
        {
            // ⚠️ Demoting the last owner would leave an organisation nobody can
            // administer, and no support tooling exists to fix it.
            if (existing.Role == OrgRole.Owner && request.Role != OrgRole.Owner)
            {
                var owners = await database.OrgMembers
                    .CountAsync(x => x.OrgId == orgId && x.Role == OrgRole.Owner, cancellationToken);

                if (owners <= 1)
                {
                    return ApiProblem.From(ApiErrors.LastOwner, "Promote somebody else to owner first.");
                }
            }

            existing.Role = request.Role;
        }

        await database.SaveChangesAsync(cancellationToken);
        await Audit.WriteAsync(
            database,
            new AuditEntry(
                orgId,
                guard.UserId,
                "member.role_set",
                "user",
                userId,
                new Dictionary<string, string> { ["role"] = request.Role.ToString() }),
            clock,
            cancellationToken);

        return TypedResults.NoContent();
    }
}
