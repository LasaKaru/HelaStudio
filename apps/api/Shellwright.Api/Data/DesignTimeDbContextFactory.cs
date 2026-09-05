using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shellwright.Api.Data;

/// <summary>
/// Supplies a context to <c>dotnet ef</c> without booting the application.
/// </summary>
/// <remarks>
/// Design-time tooling would otherwise construct the real host, which wants a
/// reachable database, a signing key, and a Redis. Generating a migration
/// should need none of those: the connection string here is never opened.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShellwrightDbContext>
{
    /// <inheritdoc />
    public ShellwrightDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SHELLWRIGHT_MIGRATION_CONNECTION")
            ?? "Host=localhost;Database=shellwright;Username=shellwright_migrator";

        var options = new DbContextOptionsBuilder<ShellwrightDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations"))
            .Options;

        return new ShellwrightDbContext(options);
    }
}
