using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Authorization;
using Shellwright.Api.Config;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;
using Shellwright.ConfigSchema;

namespace Shellwright.Api.Endpoints;

/// <summary>Create an app.</summary>
/// <param name="Name">Display name, also the name under the icon.</param>
/// <param name="BundleId">Reverse-DNS identifier, shared by both platforms.</param>
/// <param name="InitialUrl">The first page the shell loads.</param>
public sealed record CreateAppRequest(
    [param: Required, StringLength(120, MinimumLength = 1)] string Name,
    [param: Required, StringLength(155, MinimumLength = 3)] string BundleId,
    [param: Required, StringLength(2000)] string InitialUrl);

/// <summary>An app as the API reports it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="WorkspaceId">Owning workspace.</param>
/// <param name="Name">Display name.</param>
/// <param name="BundleId">Reverse-DNS identifier.</param>
/// <param name="CurrentConfigVersionId">The version considered live, if any.</param>
/// <param name="CreatedAt">Creation timestamp.</param>
public sealed record AppResponse(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    string BundleId,
    Guid? CurrentConfigVersionId,
    DateTimeOffset CreatedAt);

/// <summary>Apps within a workspace.</summary>
public static class AppEndpoints
{
    /// <summary>Maps the app endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1")
            .WithTags("Apps")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapGet("/workspaces/{workspaceId:guid}/apps", ListAsync)
            .Produces<IReadOnlyList<AppResponse>>()
            .WithSummary("List a workspace's apps.");

        group.MapPost("/workspaces/{workspaceId:guid}/apps", CreateAsync)
            .Produces<AppResponse>(StatusCodes.Status201Created)
            .WithSummary("Create an app and seed its first configuration.");

        group.MapGet("/apps/{appId:guid}", GetAsync)
            .Produces<AppResponse>()
            .WithSummary("Describe an app.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid workspaceId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        var access = await guard.ForWorkspaceAsync(workspaceId, Permissions.ReadApp, cancellationToken);
        if (AccessGuard.Reject(access) is { } denial)
        {
            return denial;
        }

        var apps = await database.Apps
            .Where(x => x.WorkspaceId == workspaceId && x.ArchivedAt == null)
            .OrderBy(x => x.Name)
            .Select(x => new AppResponse(
                x.Id,
                x.WorkspaceId,
                x.Name,
                x.BundleId,
                x.CurrentConfigVersionId,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(apps);
    }

    private static async Task<IResult> GetAsync(
        Guid appId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        var access = await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken);
        if (AccessGuard.Reject(access) is { } denial)
        {
            return denial;
        }

        var app = await database.Apps
            .Where(x => x.Id == appId)
            .Select(x => new AppResponse(
                x.Id,
                x.WorkspaceId,
                x.Name,
                x.BundleId,
                x.CurrentConfigVersionId,
                x.CreatedAt))
            .FirstAsync(cancellationToken);

        return TypedResults.Ok(app);
    }

    private static async Task<IResult> CreateAsync(
        Guid workspaceId,
        [FromBody] CreateAppRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        ConfigService configs,
        UrlSafety urls,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var access = await guard.ForWorkspaceAsync(workspaceId, Permissions.CreateApp, cancellationToken);
        if (AccessGuard.Reject(access) is { } denial)
        {
            return denial;
        }

        // ⚠️ Checked before the URL is stored, not before it is fetched.
        //
        // Nothing here makes an outbound request yet — site analysis arrives in
        // a later sprint. Refusing a private address at the point of storage is
        // the difference between one guard on one endpoint and a guard on every
        // future component that reads the field, one of which will forget.
        if (await urls.CheckAsync(request.InitialUrl, cancellationToken) is { } unsafeUrl)
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["initialUrl"] = [unsafeUrl],
            });
        }

        var orgId = await database.Workspaces
            .Where(x => x.Id == workspaceId)
            .Select(x => x.OrgId)
            .FirstAsync(cancellationToken);

        var record = new AppRecord
        {
            WorkspaceId = workspaceId,
            Name = request.Name,
            BundleId = request.BundleId,
            CreatedAt = clock.GetUtcNow(),
        };

        database.Apps.Add(record);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return ApiProblem.From(
                ApiErrors.NameTaken,
                $"'{request.BundleId}' already exists in this workspace.");
        }

        // A new app gets a first version immediately, so that "the current
        // config" is never null and every reader can stop special-casing it.
        var seed = SeedConfig(request);
        var saved = await configs.SaveAsync(
            record.Id,
            orgId,
            seed,
            guard.UserId,
            "Created",
            cancellationToken);

        if (saved.Outcome == SaveOutcome.Invalid)
        {
            // The seed is built from validated request fields, so this means the
            // caller supplied something the app-level annotations accept and the
            // schema does not — a bundle id of the wrong shape, most likely.
            // Roll the app back rather than leaving one with no configuration.
            await database.Apps.Where(x => x.Id == record.Id).ExecuteDeleteAsync(cancellationToken);
            return ProblemResults.Invalid(saved.Result);
        }

        return TypedResults.Created(
            $"/v1/apps/{record.Id}",
            new AppResponse(
                record.Id,
                record.WorkspaceId,
                record.Name,
                record.BundleId,
                saved.Version!.Id,
                record.CreatedAt));
    }

    /// <summary>
    /// The smallest configuration that validates.
    /// </summary>
    /// <remarks>
    /// Kept deliberately minimal. Every field added here is one every new app
    /// carries whether or not its author wanted it, and one more thing that
    /// changes what an app looks like depending on when it was created.
    /// </remarks>
    private static JsonObject SeedConfig(CreateAppRequest request)
    {
        var origin = Uri.TryCreate(request.InitialUrl, UriKind.Absolute, out var parsed)
            ? parsed.GetLeftPart(UriPartial.Authority)
            : request.InitialUrl;

        return new JsonObject
        {
            ["schemaVersion"] = ConfigValidator.CurrentSchemaVersion,
            ["app"] = new JsonObject
            {
                ["name"] = request.Name,
                ["bundleId"] = request.BundleId,
                ["initialUrl"] = request.InitialUrl,
                ["allowedOrigins"] = new JsonArray(JsonValue.Create(origin)),
            },
        };
    }
}
