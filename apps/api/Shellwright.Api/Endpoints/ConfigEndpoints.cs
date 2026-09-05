using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Shellwright.Api.Authorization;
using Shellwright.Api.Config;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;

namespace Shellwright.Api.Endpoints;

/// <summary>A configuration version's identity, without its body.</summary>
/// <param name="Id">Identifier.</param>
/// <param name="SchemaVersion">Schema version the body conforms to.</param>
/// <param name="CodeKey">Cache key covering everything that forces a native recompile.</param>
/// <param name="AssetKey">Cache key covering everything that needs only a resource repackage.</param>
/// <param name="ContentKey">Cache key covering everything that needs only a config patch.</param>
/// <param name="CreatedBy">Who saved it.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Message">Optional note.</param>
public sealed record VersionSummary(
    Guid Id,
    int SchemaVersion,
    string CodeKey,
    string AssetKey,
    string ContentKey,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    string? Message);

/// <summary>A configuration version, body included.</summary>
/// <param name="Version">Its identity.</param>
/// <param name="Config">The resolved document.</param>
public sealed record VersionResponse(VersionSummary Version, JsonObject Config);

/// <summary>A page of results.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">The page.</param>
/// <param name="NextCursor">Pass back as <c>cursor</c> for the following page, or null at the end.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Reading, validating, saving, and comparing configurations.</summary>
public static class ConfigEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    /// <summary>Maps the configuration endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/apps/{appId:guid}/config")
            .WithTags("Configuration")
            .RequireAuthorization();

        // ⚠️ Reads and validation get the generous read limit, saves the
        // token bucket. Validation is a write-shaped request that performs no
        // write, and the studio issues one per debounced keystroke — putting it
        // on the write limit would rate-limit typing.
        group.MapGet("/", GetCurrentAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<VersionResponse>()
            .Produces(StatusCodes.Status304NotModified)
            .WithSummary("Read the current resolved configuration.");

        group.MapPost("/", SaveAsync)
            .RequireRateLimiting(RateLimitPolicies.Write)
            .Produces<SaveResponse>(StatusCodes.Status201Created)
            .Produces<SaveResponse>()
            .WithSummary("Save a new configuration version.");

        group.MapPost("/validate", ValidateAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<ValidationResponse>()
            .WithSummary("Validate without saving.");

        group.MapGet("/versions", ListVersionsAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<Page<VersionSummary>>()
            .WithSummary("List configuration versions, newest first.");

        group.MapGet("/versions/{versionId:guid}", GetVersionAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<VersionResponse>()
            .WithSummary("Read one version.");

        group.MapGet("/diff", DiffAsync)
            .RequireRateLimiting(RateLimitPolicies.Read)
            .Produces<DiffResponse>()
            .WithSummary("Compare two versions.");

        return app;
    }

    private static async Task<IResult> GetCurrentAsync(
        Guid appId,
        HttpRequest request,
        HttpResponse response,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var current = await database.Apps
            .Where(x => x.Id == appId)
            .Select(x => x.CurrentConfigVersion)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is null)
        {
            return ApiProblem.From(ApiErrors.NoConfiguration);
        }

        // ⚠️ The entity tag is the version id, and the version id is
        // content-addressed: two saves of the same document produce the same
        // version, so the tag changes exactly when the content does. A tag
        // derived from a timestamp or a row version would change on a save that
        // changed nothing, which is the case this endpoint most wants to make
        // cheap — the studio polls it.
        var etag = new EntityTagHeaderValue($"\"{current.Id}\"");

        // ⚠️ Set on every response, including the 304. A client that is never
        // handed a tag can never send one back, so computing it and only ever
        // comparing against it makes the whole mechanism dead code that looks
        // implemented. RFC 9110 also requires the tag on a 304, so that a cache
        // holding several variants knows which one was validated.
        response.Headers.ETag = etag.ToString();

        if (request.Headers.IfNoneMatch.Any(x => x == etag.Tag.Value || x == "*"))
        {
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(new VersionResponse(Summarise(current), current.Body));
    }

    private static async Task<IResult> SaveAsync(
        Guid appId,
        HttpRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        ConfigService configs,
        Idempotency idempotency,
        CancellationToken cancellationToken)
    {
        var access = await guard.ForAppAsync(appId, Permissions.SaveConfigVersion, cancellationToken);
        if (AccessGuard.Reject(access) is { } denial)
        {
            return denial;
        }

        var body = await ReadBodyAsync(request, cancellationToken);
        var userId = guard.UserId!.Value;

        var check = await idempotency.CheckAsync(request, userId, body, cancellationToken);

        if (check.Conflict)
        {
            return ApiProblem.From(
                ApiErrors.IdempotencyKeyReused,
                "That Idempotency-Key was used for a different request body.");
        }

        if (check.Replay is { } remembered)
        {
            return TypedResults.Content(
                remembered.ResponseBody,
                "application/json",
                statusCode: remembered.StatusCode);
        }

        SaveRequest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SaveRequest>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            // The parser's own message names the offset and the token, which
            // is exactly what a person fixing it needs and carries nothing they
            // did not already send.
            return ApiProblem.From(ApiErrors.MalformedJson, exception.Message);
        }

        if (parsed?.Config is null)
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["config"] = ["A configuration document is required."],
            });
        }

        var orgId = await OrgOfAsync(database, appId, cancellationToken);

        var saved = await configs.SaveAsync(
            appId,
            orgId,
            parsed.Config,
            userId,
            parsed.Message,
            cancellationToken);

        if (saved.Outcome == SaveOutcome.Invalid)
        {
            // Not remembered against the idempotency key. A retry of a request
            // that failed validation should be revalidated, not replayed —
            // otherwise fixing the config and retrying with the same key
            // returns the old errors.
            return ProblemResults.Invalid(saved.Result);
        }

        var response = new SaveResponse(
            Summarise(saved.Version!),
            saved.Outcome == SaveOutcome.Created,
            ProblemResults.Describe(saved.Result.Warnings),
            ProblemResults.Describe(saved.Result.Info));

        // ⚠️ 200 rather than 201 when nothing changed, and the same version id.
        // Returning a new version for an identical document would make the
        // history unreadable and would miss every build cache entry for a save
        // that changed nothing.
        var status = saved.Outcome == SaveOutcome.Created
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;

        var serialised = JsonSerializer.Serialize(response, JsonOptions);
        await idempotency.RememberAsync(check, request, userId, status, serialised, cancellationToken);

        return TypedResults.Content(serialised, "application/json", statusCode: status);
    }

    private static async Task<IResult> ValidateAsync(
        Guid appId,
        HttpRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        ConfigService configs,
        CancellationToken cancellationToken)
    {
        // Read access, not write. The studio calls this while somebody is
        // typing, including people who will never be allowed to save.
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var body = await ReadBodyAsync(request, cancellationToken);

        SaveRequest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SaveRequest>(body, JsonOptions);
        }
        catch (JsonException exception)
        {
            // The parser's own message names the offset and the token, which
            // is exactly what a person fixing it needs and carries nothing they
            // did not already send.
            return ApiProblem.From(ApiErrors.MalformedJson, exception.Message);
        }

        var orgId = await OrgOfAsync(database, appId, cancellationToken);
        var validated = await configs.ValidateAsync(parsed?.Config, orgId, cancellationToken);

        // 200 even when invalid: the caller asked whether it validates, and it
        // answered. A 422 here would make the studio's happy path an error
        // handler.
        return TypedResults.Ok(new ValidationResponse(
            validated.Result.Valid,
            ProblemResults.Describe(validated.Result.Errors),
            ProblemResults.Describe(validated.Result.Warnings),
            ProblemResults.Describe(validated.Result.Info)));
    }

    private static async Task<IResult> ListVersionsAsync(
        Guid appId,
        string? cursor,
        int? limit,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = database.ConfigVersions.Where(x => x.AppId == appId);

        if (Cursor.TryDecode(cursor, out var position))
        {
            // Strictly after the last row read, in the same order. The
            // tie-break on id matters: two versions saved in the same tick
            // would otherwise both be skipped or both repeated.
            query = query.Where(x =>
                x.CreatedAt < position.CreatedAt
                || (x.CreatedAt == position.CreatedAt && x.Id.CompareTo(position.Id) < 0));
        }
        else if (!string.IsNullOrEmpty(cursor))
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["cursor"] = ["That cursor is not one this endpoint issued."],
            });
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(pageSize + 1)
            .Select(x => new VersionSummary(
                x.Id,
                x.SchemaVersion,
                x.CodeKey,
                x.AssetKey,
                x.ContentKey,
                x.CreatedBy,
                x.CreatedAt,
                x.Message))
            .ToListAsync(cancellationToken);

        // One row past the page tells us whether there is another page without
        // a second COUNT query, and without claiming there is one when the
        // total happens to be an exact multiple of the page size.
        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows[..pageSize] : rows;
        var next = hasMore ? Cursor.Encode(items[^1].CreatedAt, items[^1].Id) : null;

        return TypedResults.Ok(new Page<VersionSummary>(items, next));
    }

    private static async Task<IResult> GetVersionAsync(
        Guid appId,
        Guid versionId,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        var version = await database.ConfigVersions
            .FirstOrDefaultAsync(x => x.Id == versionId && x.AppId == appId, cancellationToken);

        return version is null
            ? ApiProblem.From(ApiErrors.NotFound)
            : TypedResults.Ok(new VersionResponse(Summarise(version), version.Body));
    }

    private static async Task<IResult> DiffAsync(
        Guid appId,
        Guid? from,
        Guid? to,
        ShellwrightDbContext database,
        AccessGuard guard,
        CancellationToken cancellationToken)
    {
        if (AccessGuard.Reject(await guard.ForAppAsync(appId, Permissions.ReadApp, cancellationToken)) is { } denial)
        {
            return denial;
        }

        if (from is null || to is null)
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["from"] = ["Both 'from' and 'to' version ids are required."],
            });
        }

        var versions = await database.ConfigVersions
            .Where(x => x.AppId == appId && (x.Id == from || x.Id == to))
            .ToListAsync(cancellationToken);

        var left = versions.Find(x => x.Id == from);
        var right = versions.Find(x => x.Id == to);

        if (left is null || right is null)
        {
            return ApiProblem.From(ApiErrors.NotFound);
        }

        return TypedResults.Ok(new DiffResponse(
            left.Id,
            right.Id,
            ConfigDiff.Between(left.Body, right.Body)));
    }

    private static async Task<Guid> OrgOfAsync(
        ShellwrightDbContext database,
        Guid appId,
        CancellationToken cancellationToken) =>
        await database.Apps
            .Where(x => x.Id == appId)
            .Select(x => x.Workspace!.OrgId)
            .FirstAsync(cancellationToken);

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        return body;
    }

    private static VersionSummary Summarise(ConfigVersion version) => new(
        version.Id,
        version.SchemaVersion,
        version.CodeKey,
        version.AssetKey,
        version.ContentKey,
        version.CreatedBy,
        version.CreatedAt,
        version.Message);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A save or validate request.</summary>
    /// <param name="Config">The configuration document.</param>
    /// <param name="Message">Optional note, in the spirit of a commit message.</param>
    private sealed record SaveRequest(JsonObject? Config, string? Message);
}

/// <summary>What a save returned.</summary>
/// <param name="Version">The version, whether newly written or already present.</param>
/// <param name="Created">False when an identical version already existed.</param>
/// <param name="Warnings">Findings that did not block the save.</param>
/// <param name="Info">Hints.</param>
public sealed record SaveResponse(
    VersionSummary Version,
    bool Created,
    IReadOnlyList<DiagnosticResponse> Warnings,
    IReadOnlyList<DiagnosticResponse> Info);

/// <summary>What a validate returned.</summary>
/// <param name="Valid">True when there are no errors.</param>
/// <param name="Errors">Findings that block a save.</param>
/// <param name="Warnings">Findings allowed through.</param>
/// <param name="Info">Hints.</param>
public sealed record ValidationResponse(
    bool Valid,
    IReadOnlyList<DiagnosticResponse> Errors,
    IReadOnlyList<DiagnosticResponse> Warnings,
    IReadOnlyList<DiagnosticResponse> Info);

/// <summary>What a diff returned.</summary>
/// <param name="From">The earlier version.</param>
/// <param name="To">The later version.</param>
/// <param name="Changes">What differs, ordered by path.</param>
public sealed record DiffResponse(Guid From, Guid To, IReadOnlyList<ConfigChange> Changes);
