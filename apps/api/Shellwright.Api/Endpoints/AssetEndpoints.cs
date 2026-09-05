using System.Security.Cryptography;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Assets;
using Shellwright.Api.Authorization;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;

namespace Shellwright.Api.Endpoints;

/// <summary>An uploaded asset as the API reports it.</summary>
/// <param name="Reference">The <c>asset://sha256-…</c> reference to put in a configuration.</param>
/// <param name="ContentType">Media type, determined from the bytes.</param>
/// <param name="Bytes">Size.</param>
/// <param name="Width">Pixel width.</param>
/// <param name="Height">Pixel height.</param>
/// <param name="HasAlpha">Whether any pixel is not fully opaque.</param>
/// <param name="Deduplicated">True when these exact bytes were already stored.</param>
public sealed record AssetResponse(
    string Reference,
    string ContentType,
    long Bytes,
    int Width,
    int Height,
    bool HasAlpha,
    bool Deduplicated);

/// <summary>Uploading icons and splash images.</summary>
public static class AssetEndpoints
{
    /// <summary>Maps the asset endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/orgs/{orgId:guid}/assets")
            .WithTags("Assets")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Write);

        group.MapPost("/", UploadAsync)
            .Produces<AssetResponse>(StatusCodes.Status201Created)
            .Produces<AssetResponse>()
            .WithSummary("Upload an image and get back an asset reference.")
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> UploadAsync(
        Guid orgId,
        HttpRequest request,
        ShellwrightDbContext database,
        AccessGuard guard,
        IAssetBlobStore blobs,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var access = await guard.ForOrgAsync(orgId, Permissions.SaveConfigVersion, cancellationToken);
        if (AccessGuard.Reject(access) is { } denial)
        {
            return denial;
        }

        // ⚠️ The length limit is applied while reading, not after. Checking
        // Content-Length would trust a header, and reading the whole body first
        // would mean a client can make the server allocate whatever it likes
        // before the limit is consulted.
        using var buffer = new MemoryStream();
        var read = await CopyBoundedAsync(request.Body, buffer, ImageProbe.MaxBytes, cancellationToken);

        if (!read)
        {
            return ApiProblem.From(
                ApiErrors.PayloadTooLarge,
                $"Images must be {ImageProbe.MaxBytes / (1024 * 1024)} MB or smaller.");
        }

        var content = buffer.ToArray();

        var refusal = ImageProbe.TryProbe(content, out var probed);

        if (refusal is not null || probed is null)
        {
            return ApiProblem.Validation(new Dictionary<string, string[]>
            {
                ["file"] = [refusal ?? "The upload could not be read as an image."],
            });
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(content));

        var existing = await database.Assets
            .FirstOrDefaultAsync(x => x.OrgId == orgId && x.Sha256 == digest, cancellationToken);

        if (existing is not null)
        {
            // Already here. Writing the row again would fail on the unique
            // index; writing the bytes again would be identical work for an
            // identical result. Content addressing makes re-uploading free.
            return TypedResults.Ok(Describe(existing, deduplicated: true));
        }

        await blobs.WriteAsync(digest, content, cancellationToken);

        var asset = new Asset
        {
            OrgId = orgId,
            Sha256 = digest,
            ContentType = probed.ContentType,
            Bytes = content.Length,
            Width = probed.Width,
            Height = probed.Height,
            HasAlpha = probed.HasAlpha,
            CreatedAt = clock.GetUtcNow(),
        };

        database.Assets.Add(asset);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two uploads of the same image at once. The bytes are already
            // written and identical, so the loser reports the winner's row.
            database.Entry(asset).State = EntityState.Detached;

            var winner = await database.Assets
                .FirstAsync(x => x.OrgId == orgId && x.Sha256 == digest, cancellationToken);

            return TypedResults.Ok(Describe(winner, deduplicated: true));
        }

        return TypedResults.Created($"/v1/orgs/{orgId}/assets/{digest}", Describe(asset, deduplicated: false));
    }

    /// <summary>Copies at most <paramref name="limit"/> bytes, reporting whether the source fitted.</summary>
    private static async Task<bool> CopyBoundedAsync(
        Stream source,
        Stream destination,
        int limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0L;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);

            if (read == 0)
            {
                return true;
            }

            total += read;

            if (total > limit)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static AssetResponse Describe(Asset asset, bool deduplicated) => new(
        $"asset://sha256-{asset.Sha256}",
        asset.ContentType,
        asset.Bytes,
        asset.Width,
        asset.Height,
        asset.HasAlpha,
        deduplicated);
}
