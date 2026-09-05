using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Npgsql;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// TC-S07-SEC-004–010 — tenant isolation over the build tables.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Raw SQL as the role the API actually connects as, for the same reason as
/// <see cref="RowLevelSecurityTests"/>: asserting isolation through an endpoint
/// proves that endpoint's WHERE clause is right today, not that the next one
/// cannot get it wrong.
/// </para>
/// <para>
/// ⚠️ The artifact cache is the table where a leak is worst. Everywhere else a
/// missing policy leaks a row; here it hands one customer the compiled binary
/// of another, because the whole purpose of a cache lookup is to return
/// somebody's artifact instead of building one.
/// </para>
/// </remarks>
/// <param name="fixture">The database fixture.</param>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class BuildRowLevelSecurityTests(PostgresFixture fixture)
{
    [Fact(DisplayName = "One tenant's builds are invisible to another")]
    public async Task BuildsAreIsolated()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        var visible = await ScalarListAsync(
            "SELECT workflow_id FROM builds",
            alpha.Tenant.UserId);

        visible.Should().Contain(alpha.WorkflowId);
        visible.Should().NotContain(beta.WorkflowId);
    }

    [Fact(DisplayName = "A cached artifact is never offered to another tenant")]
    public async Task ArtifactCacheIsIsolated()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        // ⚠️ Both tenants have an entry under the *same* three cache keys, which
        // is the case that matters: two customers whose configurations happen to
        // hash identically. Without the policy, beta's lookup finds alpha's row
        // and hands over alpha's binary.
        await StoreCacheEntryAsync(alpha, "alpha-artifact");
        await StoreCacheEntryAsync(beta, "beta-artifact");

        var visible = await ScalarListAsync(
            "SELECT artifact_reference FROM artifact_cache",
            beta.Tenant.UserId);

        visible.Should().ContainSingle().Which.Should().Be("beta-artifact");
    }

    [Fact(DisplayName = "A build's history is invisible to another tenant")]
    public async Task TransitionsAreIsolated()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        await ExecuteAsOwnerAsync(
            "INSERT INTO build_transitions (id, build_id, state, occurred_at) VALUES (@id, @build, 3, now())",
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("build", alpha.BuildId);
            });

        var visible = await ScalarListAsync(
            "SELECT build_id::text FROM build_transitions",
            beta.Tenant.UserId);

        visible.Should().BeEmpty();
    }

    [Fact(DisplayName = "Usage is invisible to another organisation")]
    public async Task UsageIsIsolated()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        await StoreUsageAsync(alpha);
        await StoreUsageAsync(beta);

        var visible = await ScalarListAsync(
            "SELECT build_id::text FROM usage_records",
            beta.Tenant.UserId);

        visible.Should().ContainSingle().Which.Should().Be(beta.BuildId.ToString());
    }

    [Fact(DisplayName = "A connection with no identity sees no builds and no artifacts")]
    public async Task NoIdentitySeesNothing()
    {
        var alpha = await SeedBuildAsync("alpha");
        await StoreCacheEntryAsync(alpha, "alpha-artifact");

        (await ScalarListAsync("SELECT workflow_id FROM builds", userId: null))
            .Should().BeEmpty();

        (await ScalarListAsync("SELECT artifact_reference FROM artifact_cache", userId: null))
            .Should().BeEmpty();
    }

    [Fact(DisplayName = "The application role cannot edit a build's history")]
    public async Task TransitionsAreAppendOnly()
    {
        var alpha = await SeedBuildAsync("alpha");

        await ExecuteAsOwnerAsync(
            "INSERT INTO build_transitions (id, build_id, state, occurred_at) VALUES (@id, @build, 3, now())",
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("build", alpha.BuildId);
            });

        // ⚠️ A missing privilege, not a missing policy. The history is the
        // answer to "why did this take eleven minutes", and one that can be
        // edited answers with whatever somebody decided it should say.
        await RefusedAsync("UPDATE build_transitions SET state = 6", alpha.Tenant.UserId);
        await RefusedAsync("DELETE FROM build_transitions", alpha.Tenant.UserId);
    }

    [Fact(DisplayName = "The application role cannot un-bill a build")]
    public async Task UsageIsAppendOnly()
    {
        var alpha = await SeedBuildAsync("alpha");
        await StoreUsageAsync(alpha);

        // ⚠️ Corrections are credits — new rows — not edits. Anything else
        // means the running system can quietly change what a customer owes,
        // and nobody auditing it later can see that it happened.
        await RefusedAsync("UPDATE usage_records SET runner_seconds = 0", alpha.Tenant.UserId);
        await RefusedAsync("DELETE FROM usage_records", alpha.Tenant.UserId);
    }

    [Fact(DisplayName = "A build cannot be recorded against another tenant's app")]
    public async Task CannotWriteIntoAnotherTenant()
    {
        var alpha = await SeedBuildAsync("alpha");
        var beta = await SeedBuildAsync("beta");

        // ⚠️ WITH CHECK, not just USING. A policy that only filters reads lets
        // a confused handler *write* a row into somebody else's tenant, which
        // is worse than reading one: it is durable.
        var connection = await fixture.OpenAsAppAsync(alpha.Tenant.UserId);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(
                """
                INSERT INTO builds
                    (id, app_id, org_id, config_version_id, platform, type, state,
                     workflow_id, cache_outcome, runner_seconds, idempotency_key, created_at)
                VALUES
                    (@id, @app, @org, @config, 0, 0, 0, @workflow, 0, 0, @key, now())
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("app", beta.Tenant.AppId);
                command.Parameters.AddWithValue("org", beta.Tenant.OrgId);
                command.Parameters.AddWithValue("config", beta.ConfigVersionId);
                command.Parameters.AddWithValue("workflow", $"smuggled-{Guid.NewGuid():N}");
                command.Parameters.AddWithValue("key", Guid.NewGuid().ToString("N"));

                var act = async () => await command.ExecuteNonQueryAsync();
                await act.Should().ThrowAsync<PostgresException>();
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this "
            + "file. The rule is kept at error severity repository-wide because it guards paths that "
            + "do take external input; there is none anywhere on this one.")]
    private async Task RefusedAsync(string sql, Guid userId)
    {
        var connection = await fixture.OpenAsAppAsync(userId);
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                var act = async () => await command.ExecuteNonQueryAsync();

                var thrown = await act.Should().ThrowAsync<PostgresException>($"'{sql}' must be refused");
                thrown.Which.SqlState.Should().Be(
                    PostgresErrorCodes.InsufficientPrivilege,
                    "the refusal must come from a missing grant, not from a syntax error");
            }
        }
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this "
            + "file. The rule is kept at error severity repository-wide because it guards paths that "
            + "do take external input; there is none anywhere on this one.")]
    private async Task<List<string>> ScalarListAsync(string sql, Guid? userId)
    {
        var results = new List<string>();

        var connection = await fixture.OpenAsAppAsync(userId);
        await using (connection.ConfigureAwait(false))
        {
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
        }

        return results;
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Every statement reaching this helper is a compile-time constant from this "
            + "file. The rule is kept at error severity repository-wide because it guards paths that "
            + "do take external input; there is none anywhere on this one.")]
    private async Task ExecuteAsOwnerAsync(string sql, Action<NpgsqlCommand> bind)
    {
        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                bind(command);
                (await command.ExecuteNonQueryAsync())
                    .Should().BeGreaterThan(0, "the seed must actually insert something");
            }
        }
    }

    private async Task StoreCacheEntryAsync(SeededBuild build, string artifactReference) =>
        await ExecuteAsOwnerAsync(
            """
            INSERT INTO artifact_cache
                (id, app_id, platform, type, code_key, asset_key, content_key,
                 artifact_reference, artifact_bytes, created_at, last_used_at)
            VALUES
                (@id, @app, 0, 0, @code, @asset, @content, @reference, 1024, now(), now())
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("app", build.Tenant.AppId);
                command.Parameters.AddWithValue("code", new string('c', 64));
                command.Parameters.AddWithValue("asset", new string('a', 64));
                command.Parameters.AddWithValue("content", new string('e', 64));
                command.Parameters.AddWithValue("reference", artifactReference);
            });

    private async Task StoreUsageAsync(SeededBuild build) =>
        await ExecuteAsOwnerAsync(
            """
            INSERT INTO usage_records
                (id, org_id, build_id, platform, runner_seconds, cache_hit, artifact_bytes, created_at)
            VALUES (@id, @org, @build, 0, 240, false, 1024, now())
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", Guid.CreateVersion7());
                command.Parameters.AddWithValue("org", build.Tenant.OrgId);
                command.Parameters.AddWithValue("build", build.BuildId);
            });

    private async Task<SeededBuild> SeedBuildAsync(string label)
    {
        var tenant = await TenantSeed.CreateAsync(fixture, label);
        var configVersionId = Guid.CreateVersion7();
        var buildId = Guid.CreateVersion7();
        var workflowId = $"{label}-workflow-{Guid.NewGuid():N}";

        await ExecuteAsOwnerAsync(
            """
            INSERT INTO config_versions
                (id, app_id, schema_version, body, code_key, asset_key, content_key, created_at)
            VALUES (@config, @app, 1, '{}'::jsonb, @code, @asset, @content, now());

            INSERT INTO builds
                (id, app_id, org_id, config_version_id, platform, type, state,
                 workflow_id, cache_outcome, runner_seconds, idempotency_key, created_at)
            VALUES (@build, @app, @org, @config, 0, 0, 0, @workflow, 0, 0, @key, now());
            """,
            command =>
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
            });

        return new SeededBuild(tenant, configVersionId, buildId, workflowId);
    }

    private sealed record SeededBuild(
        SeededTenant Tenant,
        Guid ConfigVersionId,
        Guid BuildId,
        string WorkflowId);
}
