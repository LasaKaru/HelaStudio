using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Shellwright.Api.Data;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// Every migration applies, reverses, and re-applies against a real server.
/// </summary>
/// <remarks>
/// ⚠️ The reason this matters more here than in a typical service: builds run
/// for minutes and deployments happen while they are in flight. A migration
/// whose <c>Down</c> is wrong is only discovered when something has already
/// gone wrong, at the exact moment rolling back is the only option left. It is
/// also the check that catches a hand-written <c>Up</c> whose reverse someone
/// wrote from memory — the RLS migration drops five functions and fourteen
/// policies, and nothing else would notice a missing line.
///
/// It runs against a throwaway database of its own so that dropping every
/// table cannot disturb the shared fixture.
/// </remarks>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class MigrationRoundTripTests(PostgresFixture fixture)
{
    /// <summary>TC-S06-API-001 — migrations apply and roll back cleanly.</summary>
    [Fact]
    public async Task Migrations_apply_reverse_and_reapply()
    {
        var name = $"shellwright_rt_{Guid.NewGuid():N}"[..40];
        await CreateDatabaseAsync(name);

        try
        {
            var connectionString = WithDatabase(fixture.MigratorConnectionString, name);

            (await TableNamesAsync(connectionString)).Should().BeEmpty();

            await MigrateAsync(connectionString, target: null);
            var afterUp = await TableNamesAsync(connectionString);
            afterUp.Should().Contain(["orgs", "apps", "config_versions"]);

            await MigrateAsync(connectionString, target: Migration.InitialDatabase);
            var afterDown = await TableNamesAsync(connectionString);
            afterDown.Should().NotContain("orgs");
            (await FunctionNamesAsync(connectionString)).Should().BeEmpty(
                "the row-level security migration's Down must drop every function its Up created");

            await MigrateAsync(connectionString, target: null);
            (await TableNamesAsync(connectionString)).Should().BeEquivalentTo(afterUp);
        }
        finally
        {
            await DropDatabaseAsync(name);
        }
    }

    private static async Task MigrateAsync(string connectionString, string? target)
    {
        var options = new DbContextOptionsBuilder<ShellwrightDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations"))
            .Options;

        var context = new ShellwrightDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(target);
        }
    }

    private static async Task<List<string>> TableNamesAsync(string connectionString) =>
        await QueryStringsAsync(
            connectionString,
            """
            SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__migrations'
            ORDER BY tablename
            """);

    private static async Task<List<string>> FunctionNamesAsync(string connectionString) =>
        await QueryStringsAsync(
            connectionString,
            """
            SELECT p.proname FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public'
            ORDER BY p.proname
            """);

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Callers pass compile-time constants from this file; no external input reaches it.")]
    private static async Task<List<string>> QueryStringsAsync(string connectionString, string sql)
    {
        var results = new List<string>();

        var connection = new NpgsqlConnection(connectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

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

    private async Task CreateDatabaseAsync(string name) =>
        // CREATE DATABASE cannot be parameterised, and the name is a GUID this
        // method generated, so quoting it is belt and braces rather than the
        // only defence.
        await AdminAsync($"CREATE DATABASE \"{name}\" OWNER shellwright_migrator");

    private async Task DropDatabaseAsync(string name) =>
        await AdminAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The only interpolated value is a GUID-derived database name generated in this class.")]
    private async Task AdminAsync(string sql)
    {
        var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

            var command = new NpgsqlCommand(sql, connection);
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;
}
