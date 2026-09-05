using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shellwright.Api.Data;
using Xunit;

namespace Shellwright.Api.Tests.Infrastructure;

/// <summary>
/// A real Postgres, migrated, with the two roles the control plane runs as.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ There is no in-memory substitute for these tests and there cannot be one.
/// What they check — that a policy denies a row, that a role cannot UPDATE a
/// table, that an owner is exempt from their own policies — are properties of
/// PostgreSQL, not of the model. A fake would agree with whatever we asserted.
/// </para>
/// <para>
/// If the connection strings are not already in the environment the fixture
/// runs <c>scripts/dev-postgres.sh</c> itself, so that <c>dotnet test</c> works
/// on a clean checkout without a separate setup step. It never falls back to
/// skipping: a security test that quietly does not run is worse than one that
/// fails, because the pipeline stays green either way.
/// </para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Connection string for the unprivileged role the API runs as.</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    /// <summary>Connection string for the schema-owning role migrations run as.</summary>
    public string MigratorConnectionString { get; private set; } = string.Empty;

    /// <summary>Superuser connection, used only to create the throwaway database the rollback test needs.</summary>
    public string AdminConnectionString { get; private set; } = string.Empty;

    /// <summary>Connection string for the role the build orchestrator runs as.</summary>
    public string RunnerConnectionString { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var connections = ResolveConnections();
        AppConnectionString = connections.App;
        MigratorConnectionString = connections.Migrator;
        AdminConnectionString = connections.Admin;
        RunnerConnectionString = connections.Runner;

        await ResetSchemaAsync();
        await MigrateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Opens a connection as the unprivileged application role.</summary>
    /// <param name="userId">Identity to stamp for row-level security, or null for none.</param>
    /// <returns>An open connection.</returns>
    public async Task<NpgsqlConnection> OpenAsAppAsync(Guid? userId)
    {
        var connection = new NpgsqlConnection(AppConnectionString);
        await connection.OpenAsync();
        await TenantConnectionInterceptor.ApplyAsync(connection, userId);
        return connection;
    }

    /// <summary>
    /// Opens a connection as the role the build orchestrator runs as.
    /// </summary>
    /// <remarks>
    /// ⚠️ No identity is stamped, deliberately. The orchestrator acts for no
    /// particular user, which is exactly the property its policies have to
    /// express — and exactly why it is worth testing that the property did not
    /// leak to the API's role.
    /// </remarks>
    /// <returns>An open connection.</returns>
    public async Task<NpgsqlConnection> OpenAsRunnerAsync()
    {
        var connection = new NpgsqlConnection(RunnerConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Opens a connection as the schema owner, which is exempt from every policy.</summary>
    /// <returns>An open connection.</returns>
    public async Task<NpgsqlConnection> OpenAsOwnerAsync()
    {
        var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>Builds a context bound to the application role and the given identity.</summary>
    /// <param name="userId">Identity to stamp, or null for none.</param>
    /// <returns>A context the caller disposes.</returns>
    public ShellwrightDbContext CreateContext(Guid? userId)
    {
        var tenant = new TenantContext { UserId = userId };
        var options = new DbContextOptionsBuilder<ShellwrightDbContext>()
            .UseNpgsql(AppConnectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations"))
            .AddInterceptors(new TenantConnectionInterceptor(tenant))
            .Options;

        return new ShellwrightDbContext(options);
    }

    private static (string App, string Migrator, string Admin, string Runner) ResolveConnections()
    {
        var app = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_PG_APP");
        var migrator = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_PG_MIGRATOR");
        var admin = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_PG_ADMIN");
        var runner = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_PG_RUNNER");

        if (!string.IsNullOrEmpty(app)
            && !string.IsNullOrEmpty(migrator)
            && !string.IsNullOrEmpty(admin)
            && !string.IsNullOrEmpty(runner))
        {
            return (app, migrator, admin, runner);
        }

        foreach (var line in RunSetupScript().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // The script emits `export NAME='value'` so a developer can eval it.
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

            if (name == "SHELLWRIGHT_TEST_PG_APP")
            {
                app = value;
            }
            else if (name == "SHELLWRIGHT_TEST_PG_MIGRATOR")
            {
                migrator = value;
            }
            else if (name == "SHELLWRIGHT_TEST_PG_ADMIN")
            {
                admin = value;
            }
            else if (name == "SHELLWRIGHT_TEST_PG_RUNNER")
            {
                runner = value;
            }
        }

        if (string.IsNullOrEmpty(app)
            || string.IsNullOrEmpty(migrator)
            || string.IsNullOrEmpty(admin)
            || string.IsNullOrEmpty(runner))
        {
            throw new InvalidOperationException(
                "No test database. scripts/dev-postgres.sh did not report connection strings — "
                + "install PostgreSQL, or set SHELLWRIGHT_TEST_PG_APP, SHELLWRIGHT_TEST_PG_MIGRATOR, "
                + "SHELLWRIGHT_TEST_PG_ADMIN and SHELLWRIGHT_TEST_PG_RUNNER.");
        }

        return (app, migrator, admin, runner);
    }

    private static string RunSetupScript()
    {
        var repositoryRoot = FindRepositoryRoot();
        var start = new ProcessStartInfo("bash", "scripts/dev-postgres.sh")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start bash to run scripts/dev-postgres.sh.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? stdout
            : throw new InvalidOperationException($"scripts/dev-postgres.sh failed ({process.ExitCode}): {stderr}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shellwright.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }

    /// <summary>
    /// Drops every object the migrations create, so a run starts from nothing.
    /// </summary>
    /// <remarks>
    /// Dropping and recreating the schema is cheaper and far more reliable than
    /// truncating: it also removes policies, functions, and grants, which is
    /// exactly the surface these tests are about. A leftover policy from a
    /// previous run of an older migration would otherwise make the suite pass
    /// for the wrong reason.
    /// </remarks>
    private async Task ResetSchemaAsync()
    {
        var connection = new NpgsqlConnection(MigratorConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync();

            var command = new NpgsqlCommand(
                """
                DROP SCHEMA public CASCADE;
                CREATE SCHEMA public;
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                GRANT USAGE ON SCHEMA public TO shellwright_app;
                """,
                connection);

            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task MigrateAsync()
    {
        var options = new DbContextOptionsBuilder<ShellwrightDbContext>()
            .UseNpgsql(MigratorConnectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations"))
            .Options;

        var context = new ShellwrightDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            await context.Database.MigrateAsync();
        }
    }
}

/// <summary>Shares one migrated database across every test class that needs it.</summary>
/// <remarks>
/// Named for what it defines rather than for xunit's concept, so that the type
/// name does not read as a collection type. Test classes reference
/// <see cref="Name"/>, never the class itself.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DatabaseFixtureDefinition : ICollectionFixture<PostgresFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "postgres";
}
