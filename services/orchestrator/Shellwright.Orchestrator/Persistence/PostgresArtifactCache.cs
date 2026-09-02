using Microsoft.Extensions.Options;
using Npgsql;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Persistence;

/// <summary>
/// The build cache, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Every query here is scoped to one app, and the row-level security policy
/// on <c>artifact_cache</c> is the floor beneath that. Elsewhere in this system
/// a scoping mistake leaks a row; here it hands one customer the compiled binary
/// of another, because returning somebody's artifact instead of building one is
/// the entire purpose of the table.
/// </para>
/// <para>
/// The three keys are tried in order of how much work they save, and the answer
/// is the most it can be — a content match is checked before an asset match,
/// because the first means no compiler runs at all.
/// </para>
/// </remarks>
/// <param name="options">Where the database is.</param>
public sealed class PostgresArtifactCache(IOptions<BuildStoreOptions> options) : IArtifactCache
{
    private readonly string connectionString =
        options?.Value.ConnectionString ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<CacheLookup> LookupAsync(
        Guid appId,
        BuildPlatform platform,
        BuildType type,
        BuildHashes hashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken);

            // One query rather than three round trips. The CASE ranks each
            // candidate by how much of it can be reused, and ORDER BY takes the
            // best — so a row matching all three keys always beats a row
            // matching only the code key, whatever order they were inserted in.
            var command = new NpgsqlCommand(
                """
                SELECT
                    CASE
                        WHEN content_key = @content AND asset_key = @asset THEN 3
                        WHEN asset_key = @asset THEN 2
                        ELSE 1
                    END AS rank,
                    artifact_reference,
                    artifact_bytes,
                    id
                FROM artifact_cache
                WHERE app_id = @app
                  AND platform = @platform
                  AND type = @type
                  AND code_key = @code
                ORDER BY rank DESC, last_used_at DESC
                LIMIT 1
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("app", appId);
                command.Parameters.AddWithValue("platform", (int)platform);
                command.Parameters.AddWithValue("type", (int)type);
                command.Parameters.AddWithValue("code", hashes.CodeKey);
                command.Parameters.AddWithValue("asset", hashes.AssetKey);
                command.Parameters.AddWithValue("content", hashes.ContentKey);

                var reader = await command.ExecuteReaderAsync(cancellationToken);
                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        return CacheLookup.Miss;
                    }

                    var rank = reader.GetInt32(0);
                    var reference = reader.GetString(1);
                    var bytes = reader.GetInt64(2);

                    return rank switch
                    {
                        3 => new CacheLookup(CacheOutcome.Complete, reference, bytes),
                        2 => new CacheLookup(CacheOutcome.Patch, reference, bytes),

                        // ⚠️ Warm carries no artifact reference, because nothing
                        // about the previous artifact can be reused — only the
                        // app's dependency cache is warm. Handing back a
                        // reference here would invite a caller to patch an
                        // artifact whose compiled resources are stale.
                        _ => new CacheLookup(CacheOutcome.Warm, null, 0),
                    };
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task StoreAsync(
        Guid appId,
        BuildPlatform platform,
        BuildType type,
        BuildHashes hashes,
        UploadedArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        ArgumentNullException.ThrowIfNull(artifact);

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken);

            // ⚠️ Upsert against the unique index, not a read-then-write. Two
            // builds of the same configuration finishing together is ordinary,
            // not exotic — a studio save that triggers Android and a retry that
            // races its own predecessor both produce it.
            var command = new NpgsqlCommand(
                """
                INSERT INTO artifact_cache
                    (id, app_id, platform, type, code_key, asset_key, content_key,
                     artifact_reference, artifact_bytes, created_at, last_used_at)
                VALUES
                    (@id, @app, @platform, @type, @code, @asset, @content,
                     @reference, @bytes, now(), now())
                ON CONFLICT (app_id, platform, type, code_key, asset_key, content_key)
                DO UPDATE SET last_used_at = now()
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("app", appId);
                command.Parameters.AddWithValue("platform", (int)platform);
                command.Parameters.AddWithValue("type", (int)type);
                command.Parameters.AddWithValue("code", hashes.CodeKey);
                command.Parameters.AddWithValue("asset", hashes.AssetKey);
                command.Parameters.AddWithValue("content", hashes.ContentKey);
                command.Parameters.AddWithValue("reference", artifact.ArtifactReference);
                command.Parameters.AddWithValue("bytes", artifact.Bytes);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
