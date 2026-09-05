using System;
using Temporalio.Testing;
using Xunit;

namespace Shellwright.Orchestrator.Tests.Infrastructure;

/// <summary>
/// One Temporal dev server, shared by every workflow test.
/// </summary>
/// <remarks>
/// ⚠️ Shared because starting one costs about twelve seconds, and a suite that
/// started one per test took nearly two minutes to check nine things. That is
/// the kind of number that quietly turns into "we only run those in nightly",
/// and these are the tests that check cancellation frees a runner.
///
/// Each test still gets its own worker and its own activity doubles, so they
/// share a server and nothing else. Workflow ids are unique per test, so
/// histories do not collide.
/// </remarks>
public sealed class TemporalFixture : IAsyncLifetime
{
    private WorkflowEnvironment? environment;

    /// <summary>The running server.</summary>
    /// <remarks>
    /// Named <c>Server</c> rather than <c>Environment</c> so it does not shadow
    /// <see cref="System.Environment"/> inside this class, which is where the
    /// binary lookup below reads PATH from.
    /// </remarks>
    public WorkflowEnvironment Server =>
        environment ?? throw new InvalidOperationException("The Temporal server has not started.");

    /// <summary>How many times to try starting the dev server.</summary>
    /// <remarks>
    /// ⚠️ Not a way of tolerating a broken server, and not a substitute for
    /// diagnosis. The SDK waits a fixed five seconds for the dev server to
    /// accept a connection and that budget is not settable from
    /// <see cref="WorkflowEnvironmentStartLocalOptions"/> — it lives in the
    /// SDK's core. Five seconds is ample on an idle machine and is not ample
    /// when <c>dotnet test</c> starts four test projects at once and one of
    /// them is running <c>initdb</c>, which is exactly what the nightly job
    /// does. That was observed here, once, on a cold parallel start: ten
    /// workflow tests failed together with "Connection refused".
    ///
    /// A third attempt that still cannot connect is a real failure and is
    /// allowed to surface as one.
    /// </remarks>
    private const int StartAttempts = 3;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                environment = await WorkflowEnvironment.StartLocalAsync(
                    new WorkflowEnvironmentStartLocalOptions
                    {
                        // ⚠️ Reuse a Temporal binary that is already on PATH rather than
                        // fetching one. Left to itself the SDK downloads about 40 MB on
                        // first use, which means every CI run pays for it, the suite cannot
                        // run offline, and a bad day at the download host looks like a test
                        // failure. `temporal` is installed by scripts/dev-temporal.sh and by
                        // the CI workflow.
                        DevServerOptions = ExistingBinary() is { } path
                            ? new DevServerOptions { ExistingPath = path }
                            : new DevServerOptions(),
                    });

                return;
            }
            catch (InvalidOperationException) when (attempt < StartAttempts)
            {
                // The machine is busy, not broken. Give the previous attempt's
                // process time to exit before claiming its port again.
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>Finds a Temporal CLI already installed, if there is one.</summary>
    /// <returns>The path, or null to let the SDK fetch its own.</returns>
    private static string? ExistingBinary()
    {
        var configured = Environment.GetEnvironmentVariable("SHELLWRIGHT_TEMPORAL_CLI");

        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];

        return paths
            .Select(directory => Path.Combine(directory, "temporal"))
            .FirstOrDefault(File.Exists);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (environment is not null)
        {
            await environment.ShutdownAsync();
        }
    }
}

/// <summary>Shares one Temporal server across the workflow tests.</summary>
[CollectionDefinition(Name)]
public sealed class TemporalFixtureDefinition : ICollectionFixture<TemporalFixture>
{
    /// <summary>The collection name test classes reference.</summary>
    public const string Name = "temporal";
}
