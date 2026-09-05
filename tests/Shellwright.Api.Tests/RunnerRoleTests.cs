using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Npgsql;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// TC-S07-SEC-011–017 — what the build orchestrator's database role can and
/// cannot reach.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This role is the one exception to membership scoping in the whole system,
/// so it is the one that most needs its boundary written down as tests. It runs
/// builds for everybody and therefore cannot be scoped by "who is asking" — and
/// the easy way to express that, <c>BYPASSRLS</c>, would also hand it every
/// user, every token hash and every organisation, with nothing in the schema
/// recording that anyone decided so.
/// </para>
/// <para>
/// ⚠️ The second test here is the important one. Permissive policies on a table
/// are OR'd, so a <c>USING (true)</c> policy that forgot its <c>TO</c> clause
/// would silently give the API's role every tenant's rows — the entire isolation
/// model gone, with every other test still passing.
/// </para>
/// </remarks>
/// <param name="fixture">The database fixture.</param>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class RunnerRoleTests(PostgresFixture fixture)
{
    [Fact(DisplayName = "The runner sees every tenant's builds, because it runs them")]
    public async Task RunnerSeesEveryTenant()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var visible = await ReadStringsAsync(connection, "SELECT workflow_id FROM builds");

            visible.Should().Contain(alpha.WorkflowId);
            visible.Should().Contain(beta.WorkflowId);
        }
    }

    [Fact(DisplayName = "The runner's reach did not leak to the API's role")]
    public async Task ApplicationRoleIsStillScoped()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        var connection = await fixture.OpenAsAppAsync(alpha.Tenant.UserId);
        await using (connection.ConfigureAwait(false))
        {
            var visible = await ReadStringsAsync(connection, "SELECT workflow_id FROM builds");

            visible.Should().Contain(alpha.WorkflowId);
            visible.Should().NotContain(
                beta.WorkflowId,
                "a runner policy without its TO clause would be OR'd into the API role's policies");
        }
    }

    // ⚠️ count(*) rather than a named column. PostgreSQL resolves column names
    // before it checks table privileges, so `SELECT id FROM org_members` — which
    // has a composite key and no id — fails with "undefined column" and proves
    // nothing about the grant. Asking for no columns at all makes the privilege
    // the only thing that can refuse.
    [Theory(DisplayName = "The runner cannot read identity or credential tables at all")]
    [InlineData("SELECT count(*) FROM users")]
    [InlineData("SELECT count(*) FROM orgs")]
    [InlineData("SELECT count(*) FROM org_members")]
    [InlineData("SELECT count(*) FROM api_tokens")]
    [InlineData("SELECT count(*) FROM refresh_tokens")]
    [InlineData("SELECT count(*) FROM user_tokens")]
    [InlineData("SELECT count(*) FROM oauth_identities")]
    [InlineData("SELECT count(*) FROM assets")]
    [InlineData("SELECT count(*) FROM audit_events")]
    [InlineData("SELECT count(*) FROM security_events")]
    [InlineData("SELECT count(*) FROM workspaces")]
    public async Task RunnerCannotReachIdentityTables(string statement)
    {
        // ⚠️ Refused by a missing grant rather than by an empty result. This is
        // the difference between "the orchestrator has no reason to read your
        // password hash" and "the orchestrator cannot read your password hash",
        // and only the second one survives somebody writing a convenient query.
        await RefusedAsync(statement);
    }

    [Fact(DisplayName = "The runner can read a configuration but never write one")]
    public async Task RunnerCannotWriteConfigurations()
    {
        var alpha = await SeedBuildAsync("alpha");

        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var visible = await ReadStringsAsync(connection, "SELECT id::text FROM config_versions");
            visible.Should().Contain(alpha.ConfigVersionId.ToString());
        }

        // A build that could edit what it was asked to build could produce an
        // artifact nobody requested.
        await RefusedAsync("UPDATE config_versions SET schema_version = 99");
        await RefusedAsync("DELETE FROM config_versions");
    }

    [Fact(DisplayName = "The runner writes the meter and cannot rewrite it")]
    public async Task RunnerCannotRewriteUsage()
    {
        var alpha = await SeedBuildAsync("alpha");

        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var insert = new NpgsqlCommand(
                """
                INSERT INTO usage_records
                    (id, org_id, build_id, platform, runner_seconds, cache_hit, artifact_bytes, created_at)
                VALUES (@id, @org, @build, 0, 240, false, 1024, now())
                """,
                connection);

            await using (insert.ConfigureAwait(false))
            {
                insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
                insert.Parameters.AddWithValue("org", alpha.Tenant.OrgId);
                insert.Parameters.AddWithValue("build", alpha.BuildId);
                (await insert.ExecuteNonQueryAsync()).Should().Be(1);
            }
        }

        // ⚠️ The thing that writes the meter must not be able to edit it. An
        // orchestrator that can rewrite usage is an orchestrator whose bugs are
        // indistinguishable from its corrections.
        await RefusedAsync("UPDATE usage_records SET runner_seconds = 0");
        await RefusedAsync("DELETE FROM usage_records");
    }

    [Fact(DisplayName = "Recording usage twice for one build is refused by the database")]
    public async Task UsageIsIdempotentPerBuild()
    {
        var alpha = await SeedBuildAsync("alpha");

        await InsertUsageAsRunnerAsync(alpha);

        // ⚠️ Temporal retries the metering activity on any transient failure,
        // including one that happens after the row was committed. Without the
        // unique index this second write succeeds and the customer is billed
        // twice for one build.
        var act = () => InsertUsageAsRunnerAsync(alpha);

        var thrown = await act.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact(DisplayName = "The runner role holds no BYPASSRLS")]
    public async Task RunnerHasNoBypass()
    {
        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                "SELECT rolbypassrls, rolsuper FROM pg_roles WHERE rolname = 'shellwright_runner'",
                connection);

            await using (command.ConfigureAwait(false))
            {
                var reader = await command.ExecuteReaderAsync();
                await using (reader.ConfigureAwait(false))
                {
                    (await reader.ReadAsync()).Should().BeTrue("the runner role must exist");

                    // The whole design above is decoration if either of these
                    // is true, and neither has any outward symptom.
                    reader.GetBoolean(0).Should().BeFalse("BYPASSRLS would make every policy above pointless");
                    reader.GetBoolean(1).Should().BeFalse("a superuser is exempt from everything");
                }
            }
        }
    }

    private async Task InsertUsageAsRunnerAsync(SeededBuild build)
    {
        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                INSERT INTO usage_records
                    (id, org_id, build_id, platform, runner_seconds, cache_hit, artifact_bytes, created_at)
                VALUES (@id, @org, @build, 0, 240, false, 1024, now())
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("org", build.Tenant.OrgId);
                command.Parameters.AddWithValue("build", build.BuildId);
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this "
            + "file. The rule is kept at error severity repository-wide because it guards paths that "
            + "do take external input; there is none anywhere on this one.")]
    private async Task RefusedAsync(string statement)
    {
        var connection = await fixture.OpenAsRunnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(statement, connection);
            await using (command.ConfigureAwait(false))
            {
                var act = async () => await command.ExecuteNonQueryAsync();

                var thrown = await act.Should().ThrowAsync<PostgresException>(
                    $"'{statement}' must be refused to the runner role");

                thrown.Which.SqlState.Should().Be(
                    PostgresErrorCodes.InsufficientPrivilege,
                    "the refusal must come from a missing grant, not from a missing table");
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this file.")]
    private static async Task<List<string>> ReadStringsAsync(NpgsqlConnection connection, string sql)
    {
        var results = new List<string>();

        var command = new NpgsqlCommand(sql, connection);
        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync();
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync())
                {
                    results.Add(reader.GetString(0));
                }
            }
        }

        return results;
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this file.")]
    private async Task<SeededBuild> SeedBuildAsync(string label)
    {
        var tenant = await TenantSeed.CreateAsync(fixture, label);
        var configVersionId = Guid.CreateVersion7();
        var buildId = Guid.CreateVersion7();
        var workflowId = $"{label}-workflow-{Guid.NewGuid():N}";

        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                INSERT INTO config_versions
                    (id, app_id, schema_version, body, code_key, asset_key, content_key, created_at)
                VALUES (@config, @app, 1, '{}'::jsonb, @code, @asset, @content, now());

                INSERT INTO builds
                    (id, app_id, org_id, config_version_id, platform, type, state,
                     workflow_id, cache_outcome, runner_seconds, idempotency_key, created_at)
                VALUES (@build, @app, @org, @config, 0, 0, 0, @workflow, 0, 0, @key, now());
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("config", configVersionId);
                command.Parameters.AddWithValue("app", tenant.AppId);
                command.Parameters.AddWithValue("org", tenant.OrgId);
                command.Parameters.AddWithValue("build", buildId);
                command.Parameters.AddWithValue("workflow", workflowId);
                command.Parameters.AddWithValue("key", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("code", $"{label[0]}{new string('c', 63)}");
                command.Parameters.AddWithValue("asset", $"{label[0]}{new string('a', 63)}");
                command.Parameters.AddWithValue("content", $"{label[0]}{new string('e', 63)}");
                await command.ExecuteNonQueryAsync();
            }
        }

        return new SeededBuild(tenant, configVersionId, buildId, workflowId);
    }

    private sealed record SeededBuild(
        SeededTenant Tenant,
        Guid ConfigVersionId,
        Guid BuildId,
        string WorkflowId);
}
