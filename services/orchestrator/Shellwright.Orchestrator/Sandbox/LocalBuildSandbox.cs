using System.Globalization;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Sandbox;

/// <summary>
/// Runs builds directly on the host, with no isolation whatsoever.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is not a weaker sandbox. It is the absence of one. A build run
/// through it executes the customer's Gradle configuration as the host user,
/// with the host's filesystem, the host's network, and the host's credentials.
/// A malicious dependency has everything.
/// </para>
/// <para>
/// It exists because the build activities, the log pipeline, the cache fast
/// paths, and the state machine are all worth developing and testing where no
/// container runtime is available, and because a build that has never actually
/// run is a build nobody has tested. The alternative — a mock that returns a
/// canned exit code — would test the mock.
/// </para>
/// <para>
/// The safeguard is that it refuses to construct unless
/// <see cref="SandboxOptions.AllowUnisolatedSandbox"/> is set. A deployment
/// cannot reach this class by forgetting something; it has to say so.
/// </para>
/// </remarks>
public sealed class LocalBuildSandbox : IBuildSandbox
{
    private readonly SandboxOptions settings;

    /// <summary>Creates the sandbox, refusing unless it has been explicitly allowed.</summary>
    /// <param name="options">Sandbox settings.</param>
    /// <exception cref="InvalidOperationException">The unisolated sandbox has not been allowed.</exception>
    public LocalBuildSandbox(IOptions<SandboxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        settings = options.Value;

        if (!settings.AllowUnisolatedSandbox)
        {
            throw new InvalidOperationException(
                "LocalBuildSandbox provides no isolation and runs customer build scripts directly on the "
                + "host. Set Sandbox:AllowUnisolatedSandbox to use it, and only where the configurations "
                + "being built are your own.");
        }
    }

    /// <inheritdoc />
    /// <remarks>False, and every caller that cares should check it.</remarks>
    public bool IsIsolated => false;

    /// <inheritdoc />
    public Task<Workflows.RunnerLease> PrepareAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);

        var workspace = Path.Combine(settings.WorkspaceRoot, lease.LeaseId);

        // Per app even here. The isolation is absent; the cache separation is
        // not, because a shared cache would make the cache-hit measurements
        // meaningless as well as unsafe.
        var cache = Path.Combine(
            settings.CacheRoot,
            request.AppId.ToString("N", CultureInfo.InvariantCulture),
            request.Platform.ToString());

        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(cache);

        return Task.FromResult(lease with { WorkspaceRoot = workspace, CacheRoot = cache });
    }

    /// <inheritdoc />
    public async Task<SandboxResult> RunAsync(
        Workflows.RunnerLease lease,
        SandboxCommand command,
        LogLineHandler onLine,
        Action? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(command);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in command.Environment ?? new Dictionary<string, string>())
        {
            environment[key] = value;
        }

        // The container image would carry this; without one it has to come
        // from the host.
        environment["GRADLE_USER_HOME"] = lease.CacheRoot;

        return await ProcessSandboxRunner.RunAsync(
            command.Executable,
            command.Arguments,
            command.WorkingDirectory,
            environment,
            onLine,
            onProgress,
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task DestroyAsync(Workflows.RunnerLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        // ⚠️ The workspace goes; the cache stays. Deleting the cache would
        // make every build a cold one, which is the opposite of what the
        // three-way cache key exists for.
        if (Directory.Exists(lease.WorkspaceRoot))
        {
            Directory.Delete(lease.WorkspaceRoot, recursive: true);
        }

        return Task.CompletedTask;
    }
}
