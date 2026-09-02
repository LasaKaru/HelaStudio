using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shellwright.Api.Data;
using Xunit;

namespace Shellwright.Orchestrator.Tests.Infrastructure;

/// <summary>
/// A real PostgreSQL, migrated, for the orchestrator's stores.
/// </summary>
/// <remarks>
/// ⚠️ Real, because what these stores are for is behaviour the database
/// provides and a fake cannot: ON CONFLICT against a unique index making a
/// retry a no-op, SELECT ... FOR UPDATE serialising two concurrent transitions,
/// and the runner role's grants refusing what it must not do. A fake would
/// agree with whatever the tests asserted, including if the assertion were
/// wrong.
///
/// The schema is applied by running the API's migrations through its own
/// tooling, rather than by a copy of the DDL kept here — a copy would drift,
/// and the first symptom would be tests passing against a schema production
/// does not have.
///
/// ⚠️ Its own database, not the API suite's. Both fixtures drop the schema and
/// re-migrate when they start, and `dotnet test` runs test projects in
/// parallel — so sharing one database means whichever starts second pulls the
/// schema out from under the first. That failed CI once, with errors
/// ("relation __migrations does not exist", policy violations on rows that
/// should have existed) that read like real defects in the code under test
/// rather than like two suites fighting over a database.
/// </remarks>
public sealed class BuildDatabaseFixture : IAsyncLifetime
{
    /// <summary>Connection string for the role the orchestrator runs as.</summary>
    public string RunnerConnectionString { get; private set; } = string.Empty;

    /// <summary>Connection string for the schema owner, used only to seed.</summary>
    public string OwnerConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var connections = Resolve();
        RunnerConnectionString = connections.Runner;
        OwnerConnectionString = connections.Migrator;

        await MigrateAsync(connections.Migrator);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Opens a connection as the orchestrator's role.</summary>
    /// <returns>An open connection.</returns>
    public async Task<NpgsqlConnection> OpenAsRunnerAsync()
    {
        var connection = new NpgsqlConnection(RunnerConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Opens a connection as the schema owner, which every policy exempts.</summary>
    /// <returns>An open connection.</returns>
    public async Task<NpgsqlConnection> OpenAsOwnerAsync()
    {
        var connection = new NpgsqlConnection(OwnerConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// The database this suite owns.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately not <c>shellwright_test</c>, which the API suite drops
    /// and re-migrates.
    /// </remarks>
    private const string DatabaseName = "shellwright_orchestrator_test";

    private static (string Runner, string Migrator) Resolve()
    {
        // ⚠️ The environment variables the API suite uses are ignored here, on
        // purpose. In CI they name the API's database, and honouring them would
        // put both suites back on one database — which is the failure this
        // class exists to avoid. An explicit override for this suite alone is
        // still available.
        var runner = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_ORCHESTRATOR_PG_RUNNER");
        var migrator = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_ORCHESTRATOR_PG_MIGRATOR");

        if (!string.IsNullOrWhiteSpace(runner) && !string.IsNullOrWhiteSpace(migrator))
        {
            return (runner, migrator);
        }

        // ⚠️ A different database from the API suite's, passed to the same
        // script. See the note on this class.
        foreach (var line in Run(
                     "bash",
                     "scripts/dev-postgres.sh",
                     ("SHELLWRIGHT_PG_DATABASE", DatabaseName))
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (!trimmed.StartsWith("export ", StringComparison.Ordinal))
            {
                continue;
            }

            var assignment = trimmed["export ".Length..];
            var separator = assignment.IndexOf('=', StringComparison.Ordinal);

            if (separator < 0)
            {
                continue;
            }

            var name = assignment[..separator];
            var value = assignment[(separator + 1)..].Trim('\'');

            if (name == "SHELLWRIGHT_TEST_PG_RUNNER")
            {
                runner = value;
            }
            else if (name == "SHELLWRIGHT_TEST_PG_MIGRATOR")
            {
                migrator = value;
            }
        }

        if (string.IsNullOrWhiteSpace(runner) || string.IsNullOrWhiteSpace(migrator))
        {
            throw new InvalidOperationException(
                "No test database. scripts/dev-postgres.sh reported no connection strings — install "
                + "PostgreSQL, or set SHELLWRIGHT_TEST_ORCHESTRATOR_PG_RUNNER and "
                + "SHELLWRIGHT_TEST_ORCHESTRATOR_PG_MIGRATOR.");
        }

        return (runner, migrator);
    }

    private static async Task MigrateAsync(string migratorConnectionString)
    {
        // ⚠️ Applied rather than assumed. These tests live in a different
        // project from the migrations, so nothing else guarantees the database
        // in front of them is current — and a store tested against last week's
        // schema is one that passes here and fails in production.
        //
        // ⚠️ Through the context rather than by shelling out to `dotnet ef`.
        // That tool is a global install this repository does not require, so a
        // fixture that depended on it would pass locally and fail on a CI
        // runner with a message about a missing command rather than about the
        // schema.
        var options = new DbContextOptionsBuilder<ShellwrightDbContext>()
            .UseNpgsql(migratorConnectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations"))
            .Options;

        var context = new ShellwrightDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.MigrateAsync();
        }
    }

    private static string Run(
        string fileName,
        string arguments,
        params (string Name, string Value)[] environment)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Shellwright.slnx")))
        {
            root = root.Parent;
        }

        var start = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = root!.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? stdout
            : throw new InvalidOperationException(
                $"{fileName} {arguments} failed ({process.ExitCode}).\n{stdout}\n{stderr}");
    }
}

/// <summary>Shares one migrated database across the store tests.</summary>
[CollectionDefinition(Name)]
public sealed class BuildDatabaseFixtureDefinition : ICollectionFixture<BuildDatabaseFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "build-database";
}
