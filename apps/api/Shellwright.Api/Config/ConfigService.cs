using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.ConfigSchema;
using Shellwright.ConfigSchema.Rules;

namespace Shellwright.Api.Config;

/// <summary>How a save ended.</summary>
public enum SaveOutcome
{
    /// <summary>The configuration did not validate. Nothing was written.</summary>
    Invalid = 0,

    /// <summary>An identical version already existed and was returned instead.</summary>
    Unchanged = 1,

    /// <summary>A new version was written.</summary>
    Created = 2,
}

/// <summary>The result of saving a configuration.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Version">The version, whether newly written or already present.</param>
/// <param name="Result">Every diagnostic, including warnings on a successful save.</param>
public sealed record SaveResult(SaveOutcome Outcome, ConfigVersion? Version, ValidationResult Result);

/// <summary>
/// Validating, canonicalising, hashing, and storing configurations.
/// </summary>
/// <param name="database">The database context.</param>
/// <param name="hashContext">Deployment facts that feed the cache key.</param>
/// <param name="assets">Asset metadata, so icon rules can run server-side.</param>
/// <param name="clock">Time source.</param>
public sealed class ConfigService(
    ShellwrightDbContext database,
    HashContextProvider hashContext,
    IAssetResolverFactory assets,
    TimeProvider clock)
{
    /// <summary>Validates a configuration without touching the database.</summary>
    /// <param name="config">The document as submitted.</param>
    /// <param name="orgId">Organisation whose assets the icon rules may consult.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The diagnostics and the resolved document.</returns>
    /// <remarks>
    /// The studio calls this on every debounced keystroke, so it must stay a
    /// pure function of the document plus a small asset lookup. Anything that
    /// writes belongs in <see cref="SaveAsync"/>.
    /// </remarks>
    public async Task<ValidatedConfig> ValidateAsync(
        JsonNode? config,
        Guid orgId,
        CancellationToken cancellationToken = default)
    {
        var resolver = await assets.CreateAsync(orgId, cancellationToken);
        return new ConfigValidator(assets: resolver).Validate(config);
    }

    /// <summary>
    /// Saves a configuration, returning the existing version when nothing changed.
    /// </summary>
    /// <param name="appId">The app to save against.</param>
    /// <param name="orgId">Organisation the app belongs to.</param>
    /// <param name="config">The document as submitted.</param>
    /// <param name="actorId">Who is saving.</param>
    /// <param name="message">Optional note, in the spirit of a commit message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened, and the version.</returns>
    /// <remarks>
    /// <para>
    /// ⚠️ Idempotence is enforced by a unique index over the three cache keys,
    /// not by a read-then-write. The obvious implementation — look for a
    /// matching version, insert if absent — is a race that produces duplicate
    /// versions under exactly the concurrency a studio with autosave generates.
    /// The insert is attempted and a unique violation is read as "somebody
    /// else already saved this", which is both correct and one round trip.
    /// </para>
    /// <para>
    /// Warnings are returned on success rather than swallowed. A save that
    /// quietly drops "this permission has no justification" is how an app
    /// reaches store review with it still there.
    /// </para>
    /// </remarks>
    public async Task<SaveResult> SaveAsync(
        Guid appId,
        Guid orgId,
        JsonNode? config,
        Guid? actorId,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(config, orgId, cancellationToken);

        if (!validated.Result.Valid)
        {
            return new SaveResult(SaveOutcome.Invalid, null, validated.Result);
        }

        var hashes = ConfigHasher.Compute(validated.Resolved, hashContext.Create());

        var existing = await database.ConfigVersions
            .FirstOrDefaultAsync(
                x => x.AppId == appId
                    && x.CodeKey == hashes.CodeKey
                    && x.AssetKey == hashes.AssetKey
                    && x.ContentKey == hashes.ContentKey,
                cancellationToken);

        if (existing is not null)
        {
            return new SaveResult(SaveOutcome.Unchanged, existing, validated.Result);
        }

        var version = new ConfigVersion
        {
            AppId = appId,
            SchemaVersion = ConfigValidator.CurrentSchemaVersion,

            // ⚠️ Stored canonicalised, then re-parsed. jsonb normalises what it
            // stores — key order, whitespace, number formatting — so writing the
            // document as submitted and reading it back would not round-trip.
            // Canonicalising first makes the stored form the same one the hashes
            // were computed over, which is what lets a reader verify them.
            Body = ReparseCanonical(validated.Resolved),
            CodeKey = hashes.CodeKey,
            AssetKey = hashes.AssetKey,
            ContentKey = hashes.ContentKey,
            CreatedBy = actorId,
            CreatedAt = clock.GetUtcNow(),
            Message = message,
        };

        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await using (transaction.ConfigureAwait(false))
        {
            database.ConfigVersions.Add(version);

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsUniqueViolation())
            {
                // Lost the race. Whoever won wrote a byte-identical version, so
                // returning theirs is the same answer.
                database.Entry(version).State = EntityState.Detached;
                await transaction.RollbackAsync(cancellationToken);

                var winner = await database.ConfigVersions.FirstAsync(
                    x => x.AppId == appId
                        && x.CodeKey == hashes.CodeKey
                        && x.AssetKey == hashes.AssetKey
                        && x.ContentKey == hashes.ContentKey,
                    cancellationToken);

                return new SaveResult(SaveOutcome.Unchanged, winner, validated.Result);
            }

            await database.Apps
                .Where(x => x.Id == appId)
                .ExecuteUpdateAsync(
                    x => x.SetProperty(a => a.CurrentConfigVersionId, version.Id),
                    cancellationToken);

            await Audit.WriteAsync(
                database,
                new AuditEntry(
                    orgId,
                    actorId,
                    "config.version.created",
                    "app",
                    appId,
                    new Dictionary<string, string>
                    {
                        ["versionId"] = version.Id.ToString(),

                        // The cache keys, not the body. The body is large, it is
                        // already stored, and the audit trail is exported.
                        ["codeKey"] = hashes.CodeKey,
                        ["assetKey"] = hashes.AssetKey,
                        ["contentKey"] = hashes.ContentKey,
                    }),
                clock,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        return new SaveResult(SaveOutcome.Created, version, validated.Result);
    }

    /// <summary>
    /// Round-trips a document through its canonical form.
    /// </summary>
    /// <param name="resolved">The resolved document.</param>
    /// <returns>An equivalent document built from canonical bytes.</returns>
    private static JsonObject ReparseCanonical(JsonObject resolved) =>
        JsonNode.Parse(CanonicalJson.Serialize(resolved)) as JsonObject ?? [];
}

/// <summary>Builds an asset resolver scoped to one organisation.</summary>
public interface IAssetResolverFactory
{
    /// <summary>Creates a resolver over the organisation's uploaded assets.</summary>
    /// <param name="orgId">The organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A resolver.</returns>
    Task<IAssetResolver> CreateAsync(Guid orgId, CancellationToken cancellationToken = default);
}

/// <summary>Resolves assets from the organisation's uploads.</summary>
/// <param name="database">The database context.</param>
public sealed class DatabaseAssetResolverFactory(ShellwrightDbContext database) : IAssetResolverFactory
{
    /// <inheritdoc />
    public async Task<IAssetResolver> CreateAsync(Guid orgId, CancellationToken cancellationToken = default)
    {
        // Loaded up front rather than queried per reference: a configuration
        // names at most a handful of assets, an organisation holds few, and a
        // rule that issues a query per lookup turns validation into an N+1 on
        // the studio's keystroke path.
        var rows = await database.Assets
            .Where(x => x.OrgId == orgId)
            .Select(x => new { x.Sha256, x.Width, x.Height, x.HasAlpha })
            .ToListAsync(cancellationToken);

        var byDigest = rows.ToDictionary(
            x => x.Sha256,
            x => new AssetMetadata(x.Width, x.Height, x.HasAlpha),
            StringComparer.Ordinal);

        return new DictionaryAssetResolver(byDigest);
    }
}

/// <summary>An asset resolver over a fixed map of digests.</summary>
/// <param name="byDigest">Metadata keyed by lowercase hex digest.</param>
public sealed class DictionaryAssetResolver(IReadOnlyDictionary<string, AssetMetadata> byDigest) : IAssetResolver
{
    private const string Prefix = "asset://sha256-";

    /// <inheritdoc />
    public AssetMetadata? Lookup(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!reference.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return byDigest.TryGetValue(reference[Prefix.Length..], out var metadata) ? metadata : null;
    }
}
