using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Authorization;
using Shellwright.Api.Builds;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;

namespace Shellwright.Api.Endpoints;

/// <summary>What a caller asks for when starting a build.</summary>
/// <param name="Platform">Which platform to build.</param>
/// <param name="Type">Debug or release.</param>
/// <param name="ConfigVersionId">
/// The exact version to build, or null for the app's current one.
/// </param>
public sealed record StartBuildRequest(
    BuildPlatform Platform,
    BuildType Type,
    Guid? ConfigVersionId);

/// <summary>A build, as the API reports it.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="AppId">Which app.</param>
/// <param name="ConfigVersionId">Exactly what was built.</param>
/// <param name="Platform">Which platform.</param>
/// <param name="Type">Debug or release.</param>
/// <param name="State">Where it has got to.</param>
/// <param name="CacheOutcome">How much of a previous build was reused.</param>
/// <param name="RunnerSeconds">Metered runner time.</param>
/// <param name="FailureCode">A stable code for why it failed, or null.</param>
/// <param name="FailureMessage">What a person can do about it, or null.</param>
/// <param name="ArtifactBytes">Size of what it produced, or null.</param>
/// <param name="CreatedAt">When it was accepted.</param>
/// <param name="StartedAt">When a runner picked it up, or null.</param>
/// <param name="FinishedAt">When it ended, or null.</param>
public sealed record BuildResponse(
    Guid Id,
    Guid AppId,
    Guid ConfigVersionId,
    BuildPlatform Platform,
    BuildType Type,
    BuildState State,
    BuildCacheOutcome CacheOutcome,
    int RunnerSeconds,
    string? FailureCode,
    string? FailureMessage,
    long? ArtifactBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Where to fetch a finished artifact.</summary>
/// <param name="Url">A short-lived link.</param>
/// <param name="ExpiresIn">How many seconds it stays valid.</param>
/// <param name="Bytes">How large the download is.</param>
public sealed record ArtifactLinkResponse(string Url, int ExpiresIn, long Bytes);

/// <summary>Starting, watching, and cancelling builds.</summary>
public static class BuildEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    /// <summary>Maps the build endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapBuildEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/apps/{appId:guid}/builds")
            .WithTags("Builds")
            .RequireAuthorization();

        group.MapPost("/", StartAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .Produces<BuildResponse>(StatusCodes.Status202Accepted)
            .WithSummary("Start a build. Requires an Idempotency-Key.");

        group.MapGet("/", ListAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<Page<BuildResponse>>()
            .WithSummary("List builds, newest first.");

        group.MapGet("/{buildId:guid}", GetAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<BuildResponse>()
            .WithSummary("Read one build.");

        group.MapPost("/{buildId:guid}/cancel", CancelAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .Produces<BuildResponse>()
            .WithSummary("Ask a running build to stop.");

        group.MapGet("/{buildId:guid}/artifact", ArtifactAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<ArtifactLinkResponse>()
            .WithSummary("Get a short-lived download link for a finished build.");

        // ⚠️ Anonymous, and the signature is the credential.
        //
        // This is deliberate, not an oversight: an artifact is downloaded by a
        // browser, a `curl` in somebody's CI, or an emulator — none of which
        // can be relied upon to carry a bearer token, and all of which log the
        // URL. A signed, 15-minute link that names one build and one artifact
        // is a narrower grant than a token that opens the whole API and lives
        // for an hour.
        //
        // EndpointAuthorizationTests enumerates every anonymous route by name,
        // so adding one is a deliberate line in a diff rather than a thing that
        // happens.
        group.MapGet("/{buildId:guid}/artifact/download", DownloadAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Read)
            .WithSummary("Download an artifact using a signed link.");

        return app;
    }

    private static async Task<IResult> StartAsync(
        Guid appId,
        StartBuildRequest body,
        HttpRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        BuildLauncher launcher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.TriggerBuild, cancellationToken))
            is { } denial)
        {
            return denial;
        }

        // ⚠️ Required here, unlike everywhere else in this API. A retried save
        // costs a duplicate row that the content address collapses anyway; a
        // retried build costs runner minutes somebody is billed for, and the
        // server has no other way to tell "start another build" from "I did not
        // hear you". Rejecting the request is friendlier than the alternative,
        // which is a duplicate charge nobody notices until the invoice.
        var idempotencyKey = request.Headers[Idempotency.HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ApiProblem.From(
                ApiErrors.IdempotencyKeyRequired,
                "Send an Idempotency-Key header so a retry cannot start a second build.");
        }

        var app = await database.Apps
            .Where(x => x.Id == appId)
            .Select(x => new { x.Id, x.WorkspaceId, x.CurrentConfigVersionId, x.ArchivedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (app is null)
        {
            return ApiProblem.From(ApiErrors.NotFound);
        }

        if (app.ArchivedAt is not null)
        {
            return ApiProblem.From(
                ApiErrors.AppArchived,
                "This app has been archived. Restore it before building.");
        }

        var configVersionId = body.ConfigVersionId ?? app.CurrentConfigVersionId;

        if (configVersionId is null)
        {
            return ApiProblem.From(ApiErrors.NoConfiguration);
        }

        // ⚠️ Checked against this app, not merely for existence. Without this a
        // caller could name any version id they had ever seen and have it built
        // under an app they do control — row-level security hides other
        // tenants' versions, so the observable failure would be a confusing
        // 404, but the check belongs here where the reason is legible.
        var versionBelongs = await database.ConfigVersions
            .AnyAsync(x => x.Id == configVersionId && x.AppId == appId, cancellationToken);

        if (!versionBelongs)
        {
            return ApiProblem.From(
                ApiErrors.NotFound,
                "That configuration version does not belong to this app.");
        }

        var orgId = await database.Workspaces
            .Where(x => x.Id == app.WorkspaceId)
            .Select(x => x.OrgId)
            .FirstAsync(cancellationToken);

        var outcome = await launcher.StartAsync(
            new BuildLaunch(
                appId,
                orgId,
                configVersionId.Value,
                body.Platform,
                body.Type,
                guard.UserId,
                idempotencyKey),
            cancellationToken);

        return outcome switch
        {
            { Existing: { } existing } => TypedResults.Ok(ToResponse(existing)),
            { ConcurrencyExceeded: true } => ApiProblem.From(
                ApiErrors.BuildConcurrencyExceeded,
                "This organisation already has as many builds running as its plan allows. "
                + "Wait for one to finish, or cancel it."),
            _ => TypedResults.Accepted(
                $"/v1/apps/{appId}/builds/{outcome.Started!.Id}",
                ToResponse(outcome.Started)),
        };
    }

    private static async Task<IResult> GetAsync(
        Guid appId,
        Guid buildId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken))
            is { } denial)
        {
            return denial;
        }

        var build = await database.Builds
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == buildId && x.AppId == appId, cancellationToken);

        return build is null
            ? ApiProblem.From(ApiErrors.NotFound)
            : TypedResults.Ok(ToResponse(build));
    }

    private static async Task<IResult> ListAsync(
        Guid appId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken,
        string? cursor = null,
        int? limit = null)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken))
            is { } denial)
        {
            return denial;
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = database.Builds
            .AsNoTracking()
            .Where(x => x.AppId == appId);

        // Keyset rather than offset: a build list is written to constantly, and
        // an offset page shifts under the reader every time one starts.
        if (Cursor.TryDecode(cursor, out var position))
        {
            query = query.Where(x =>
                x.CreatedAt < position.CreatedAt
                || (x.CreatedAt == position.CreatedAt && x.Id.CompareTo(position.Id) < 0));
        }

        var page = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToList();

        return TypedResults.Ok(new Page<BuildResponse>(
            items.Select(ToResponse).ToList(),
            hasMore && items.Count > 0
                ? Cursor.Encode(items[^1].CreatedAt, items[^1].Id)
                : null));
    }

    private static async Task<IResult> CancelAsync(
        Guid appId,
        Guid buildId,
        ShellwrightDbContext database,
        AccessGuard guard,
        BuildLauncher launcher,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.TriggerBuild, cancellationToken))
            is { } denial)
        {
            return denial;
        }

        var build = await database.Builds
            .FirstOrDefaultAsync(x => x.Id == buildId && x.AppId == appId, cancellationToken);

        if (build is null)
        {
            return ApiProblem.From(ApiErrors.NotFound);
        }

        if (IsTerminal(build.State))
        {
            // ⚠️ 409 rather than a silent 200. Cancelling a finished build is
            // almost always a mistake about which build is which, and answering
            // "fine" leaves the caller believing they stopped something.
            return ApiProblem.From(
                ApiErrors.BuildNotCancellable,
                $"This build is already {build.State}.");
        }

        // ⚠️ The state is not written here. Temporal owns whether the workflow
        // actually stopped, and the transition to Cancelled is recorded by the
        // activity that runs when it does. Writing Cancelled optimistically
        // would let the row say "stopped" while a runner kept burning minutes.
        await launcher.CancelAsync(build.WorkflowId, cancellationToken);

        return TypedResults.Ok(ToResponse(build));
    }

    private static async Task<IResult> ArtifactAsync(
        Guid appId,
        Guid buildId,
        HttpRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        ArtifactLinks links,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken))
            is { } denial)
        {
            return denial;
        }

        var build = await database.Builds
            .AsNoTracking()
            .Where(x => x.Id == buildId && x.AppId == appId)
            .Select(x => new { x.ArtifactReference, x.ArtifactBytes })
            .FirstOrDefaultAsync(cancellationToken);

        if (build is null)
        {
            return ApiProblem.From(ApiErrors.NotFound);
        }

        if (build.ArtifactReference is null)
        {
            return ApiProblem.From(
                ApiErrors.NoArtifact,
                "This build produced no artifact. If it failed, the log says why.");
        }

        var query = links.Issue(buildId, build.ArtifactReference);

        return TypedResults.Ok(new ArtifactLinkResponse(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{request.Scheme}://{request.Host}/v1/apps/{appId}/builds/{buildId}/artifact/download?{query}"),
            (int)ArtifactLinks.Lifetime.TotalSeconds,
            build.ArtifactBytes ?? 0));
    }

    private static async Task<IResult> DownloadAsync(
        Guid appId,
        Guid buildId,
        HttpRequest request,
        ShellwrightDbContext database,
        ArtifactLinks links,
        IArtifactBytes artifacts,
        CancellationToken cancellationToken)
    {
        // ⚠️ Read as the schema owner would not be acceptable, so this query
        // runs with no tenant identity — which means row-level security hides
        // every build. The lookup therefore goes through a service that reads
        // by build id alone, and the *only* thing standing between a caller and
        // an artifact is the signature checked below. That is the whole design:
        // one narrow, signed, expiring grant instead of a session.
        var artifact = await artifacts.FindAsync(appId, buildId, cancellationToken);

        if (artifact is null)
        {
            // ⚠️ The same answer for "no such build" and "build produced
            // nothing". An unauthenticated endpoint that distinguishes them is
            // an oracle for which build ids exist.
            return ApiProblem.From(ApiErrors.NotFound);
        }

        if (!links.IsValid(
                buildId,
                artifact.Reference,
                request.Query["expires"],
                request.Query["signature"]))
        {
            return ApiProblem.From(
                ApiErrors.InvalidDownloadLink,
                "This link has expired or was not issued by this server. Ask for a new one.");
        }

        var content = await artifacts.OpenAsync(artifact.Reference, cancellationToken);

        return content is null
            ? ApiProblem.From(ApiErrors.NoArtifact)
            : TypedResults.Stream(
                content,
                "application/vnd.android.package-archive",
                $"{artifact.FileName}");
    }

    private static bool IsTerminal(BuildState state) =>
        state is BuildState.Succeeded or BuildState.Failed or BuildState.Cancelled;

    private static BuildResponse ToResponse(Build build) =>
        new(
            build.Id,
            build.AppId,
            build.ConfigVersionId,
            build.Platform,
            build.Type,
            build.State,
            build.CacheOutcome,
            build.RunnerSeconds,
            build.FailureCode,
            build.FailureMessage,
            build.ArtifactBytes,
            build.CreatedAt,
            build.StartedAt,
            build.FinishedAt);
}
