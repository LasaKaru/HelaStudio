using System.Diagnostics;
using StackExchange.Redis;
using Xunit;

namespace Shellwright.Orchestrator.Tests.Infrastructure;

/// <summary>
/// A real Redis for the log pipeline's tests.
/// </summary>
/// <remarks>
/// ⚠️ Real, because the behaviour under test is Redis's: batched adds,
/// approximate stream trimming, and resuming a read from a stream id. A fake
/// would agree with whatever these tests asserted, including if the assertion
/// were wrong.
///
/// The fixture starts one itself when no connection string is in the
/// environment, so `dotnet test` works on a clean checkout.
/// </remarks>
public sealed class RedisFixture : IAsyncLifetime
{
    private ConnectionMultiplexer? connection;

    /// <summary>The connection every test shares.</summary>
    public IConnectionMultiplexer Connection =>
        connection ?? throw new InvalidOperationException("Redis has not started.");

    /// <inheritdoc />
    public async Task InitializeAsync() =>
        connection = await ConnectionMultiplexer.ConnectAsync(Resolve());

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    private static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEST_REDIS");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        foreach (var line in RunSetupScript().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("export SHELLWRIGHT_TEST_REDIS=", StringComparison.Ordinal))
            {
                return trimmed["export SHELLWRIGHT_TEST_REDIS=".Length..].Trim('\'');
            }
        }

        throw new InvalidOperationException(
            "No Redis. scripts/dev-redis.sh reported no connection string — install redis-server, "
            + "or set SHELLWRIGHT_TEST_REDIS.");
    }

    private static string RunSetupScript()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Shellwright.slnx")))
        {
            root = root.Parent;
        }

        var start = new ProcessStartInfo("bash", "scripts/dev-redis.sh")
        {
            WorkingDirectory = root!.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start bash to run scripts/dev-redis.sh.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? stdout
            : throw new InvalidOperationException($"scripts/dev-redis.sh failed ({process.ExitCode}): {stderr}");
    }
}

/// <summary>Shares one Redis across the log pipeline tests.</summary>
[CollectionDefinition(Name)]
public sealed class RedisFixtureDefinition : ICollectionFixture<RedisFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "redis";
}
