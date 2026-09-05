using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Npgsql;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Persistence;

/// <summary>Where the orchestrator's database is.</summary>
public sealed class BuildStoreOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "BuildStore";

    /// <summary>
    /// Connection string for the <c>shellwright_runner</c> role.
    /// </summary>
    /// <remarks>
    /// ⚠️ The runner role, never the migrator and never a superuser. The
    /// orchestrator's reach is bounded by that role's grants — it cannot read a
    /// user, an organisation, or any credential table — and connecting as
    /// anything else silently discards the whole of
    /// <c>Data/Sql/RunnerRole.up.sql</c>.
    /// </remarks>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// The build record, in PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Postgres is the record of what happened; Temporal is what makes it
/// happen. Querying a workflow works until its history is archived, cannot be
/// joined to a tenant, and cannot be paginated — so the customer-facing answer
/// to "how is my build going" comes from a table.
/// </para>
/// <para>
/// ⚠️ Raw SQL rather than the API's DbContext, and deliberately. The two
/// services deploy separately; sharing a context would make a migration in one
/// a runtime break in the other, and would hand the orchestrator a typed door
/// to every table the runner role is specifically not allowed to touch.
/// </para>
/// </remarks>
/// <param name="options">Where the database is.</param>
public sealed class PostgresBuildStore(IOptions<BuildStoreOptions> options) : IBuildStore
{
    private readonly string connectionString =
        options?.Value.ConnectionString ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<StoredConfig?> LoadConfigAsync(
        Guid configVersionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        await using (connection.ConfigureAwait(false))
        {
            // ⚠️ No join to workspaces, and not as an optimisation. The runner
            // role has no grant on that table, because an orchestrator has no
            // business enumerating a customer's organisation structure in order
            // to compile a project. An earlier version of this query joined
            // through it to resolve the organisation — a value nothing read,
            // since who is charged travels on the BuildRequest — and every
            // configuration load failed with "permission denied for table
            // workspaces".
            var command = new NpgsqlCommand(
                """
                SELECT c.app_id, c.body::text
                FROM config_versions c
                WHERE c.id = @id
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", configVersionId);

                var reader = await command.ExecuteReaderAsync(cancellationToken);
                await using (reader.ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync(cancellationToken))
                    {
                        return null;
                    }

                    var body = JsonNode.Parse(reader.GetString(1))?.AsObject()
                        ?? throw new InvalidOperationException(
                            $"Configuration version {configVersionId} does not hold a JSON object.");

                    return new StoredConfig(reader.GetGuid(0), body);
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordTransitionAsync(
        Guid buildId,
        BuildState state,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (transaction.ConfigureAwait(false))
            {
                var current = await CurrentStateAsync(connection, transaction, buildId, cancellationToken);

                // ⚠️ The legality check is here rather than in a database
                // constraint because the table it would need to consult is the
                // one being written. Doing it inside the transaction that reads
                // the current state, with the row locked, is what makes it
                // more than a suggestion.
                if (current == state)
                {
                    // Temporal replays activities. Recording the same move
                    // twice is a retry, not an illegal transition, and treating
                    // it as one would fail builds for succeeding.
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                BuildStateMachine.Transition(current, state);

                await ApplyStateAsync(connection, transaction, buildId, state, cancellationToken);
                await AppendTransitionAsync(connection, transaction, buildId, state, cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(
        Guid buildId,
        BuildFailure failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var connection = await OpenAsync(cancellationToken);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                UPDATE builds
                SET failure_code = @code, failure_message = @message
                WHERE id = @id
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", buildId);
                command.Parameters.AddWithValue("code", failure.Code);

                // ⚠️ Truncated rather than allowed to fail the write. A failure
                // reason arriving longer than the column is a bad day made
                // worse: the build already failed, and losing why is not an
                // improvement on losing the last two thousand characters.
                command.Parameters.AddWithValue("message", Truncate(failure.Message, 2000));

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordArtifactAsync(
        Guid buildId,
        UploadedArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var connection = await OpenAsync(cancellationToken);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                UPDATE builds
                SET artifact_reference = @reference, artifact_bytes = @bytes
                WHERE id = @id
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", buildId);
                command.Parameters.AddWithValue("reference", artifact.ArtifactReference);
                command.Parameters.AddWithValue("bytes", artifact.Bytes);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(UsageRecord usage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var connection = await OpenAsync(cancellationToken);
        await using (connection.ConfigureAwait(false))
        {
            // ⚠️ ON CONFLICT DO NOTHING against the unique index on build_id.
            // Temporal retries this activity on any transient failure, including
            // one that happens after the row was committed — so the second
            // attempt must be a no-op rather than a second charge. Doing this
            // as a read-then-write would still double-bill under the retry that
            // races itself.
            var command = new NpgsqlCommand(
                """
                INSERT INTO usage_records
                    (id, org_id, build_id, platform, runner_seconds, cache_hit, artifact_bytes, created_at)
                VALUES (@id, @org, @build, @platform, @seconds, @cacheHit, @bytes, now())
                ON CONFLICT (build_id) DO NOTHING
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("org", usage.OrgId);
                command.Parameters.AddWithValue("build", usage.BuildId);
                command.Parameters.AddWithValue("platform", (int)usage.Platform);
                command.Parameters.AddWithValue("seconds", usage.RunnerSeconds);
                command.Parameters.AddWithValue("cacheHit", usage.CacheHit);
                command.Parameters.AddWithValue("bytes", usage.ArtifactBytes);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static string Truncate(string value, int limit) =>
        value.Length <= limit ? value : value[..limit];

    private static async Task<BuildState> CurrentStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid buildId,
        CancellationToken cancellationToken)
    {
        // ⚠️ FOR UPDATE. Two activities reporting a transition at once would
        // otherwise both read the old state, both find their move legal, and
        // both write — producing a build that went from Building to Succeeded
        // and to Failed.
        var command = new NpgsqlCommand(
            "SELECT state FROM builds WHERE id = @id FOR UPDATE",
            connection,
            transaction);

        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("id", buildId);

            var value = await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException($"Build {buildId} does not exist.");

            return (BuildState)Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static async Task ApplyStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid buildId,
        BuildState state,
        CancellationToken cancellationToken)
    {
        // started_at and finished_at are stamped from the state rather than by
        // the caller, so "how long did this take" cannot disagree with "what
        // happened".
        //
        // ⚠️ Which states are terminal is decided in C# and passed as a flag,
        // never written as a list of numbers in the SQL. A literal `IN (4, 5, 6)`
        // here is a copy of the enum that no compiler checks, and the first
        // version of this file had one that was wrong — it would have left every
        // successful build with no finish time.
        var command = new NpgsqlCommand(
            """
            UPDATE builds
            SET state = @state,
                started_at = CASE
                    WHEN started_at IS NULL AND NOT @queued THEN now()
                    ELSE started_at
                END,
                finished_at = CASE
                    WHEN @terminal THEN now()
                    ELSE finished_at
                END
            WHERE id = @id
            """,
            connection,
            transaction);

        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("id", buildId);
            command.Parameters.AddWithValue("state", (int)state);
            command.Parameters.AddWithValue("queued", state == BuildState.Queued);
            command.Parameters.AddWithValue("terminal", BuildStateMachine.IsTerminal(state));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AppendTransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid buildId,
        BuildState state,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(
            """
            INSERT INTO build_transitions (id, build_id, state, occurred_at)
            VALUES (@id, @build, @state, now())
            """,
            connection,
            transaction);

        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("build", buildId);
            command.Parameters.AddWithValue("state", (int)state);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
