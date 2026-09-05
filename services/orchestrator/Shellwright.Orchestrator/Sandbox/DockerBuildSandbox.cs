using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Sandbox;

/// <summary>Sandbox settings.</summary>
public sealed class SandboxOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Sandbox";

    /// <summary>The runner image builds run in.</summary>
    [Required]
    public string Image { get; set; } = "shellwright/runner-android:latest";

    /// <summary>The egress-restricted Docker network to attach containers to.</summary>
    [Required]
    public string Network { get; set; } = "shellwright-build";

    /// <summary>Where per-build workspaces are created on the host.</summary>
    [Required]
    public string WorkspaceRoot { get; set; } = "/var/lib/shellwright/workspaces";

    /// <summary>Where per-app Gradle caches live on the host.</summary>
    [Required]
    public string CacheRoot { get; set; } = "/var/lib/shellwright/caches";

    /// <summary>
    /// Whether a sandbox that does not isolate may be used.
    /// </summary>
    /// <remarks>
    /// ⚠️ Defaults to false and must be set deliberately. It exists so the
    /// build activities can be developed and tested where there is no container
    /// runtime; a deployment that turns it on is running strangers' build
    /// scripts directly on the host.
    /// </remarks>
    public bool AllowUnisolatedSandbox { get; set; }
}

/// <summary>
/// Runs each build in its own container, destroyed afterwards.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is the only sandbox fit to run a customer's configuration. Every
/// isolation property the product depends on comes from the flags in
/// <see cref="SandboxHardening"/>: a read-only root filesystem, no
/// capabilities, no new privileges, an unprivileged user, bounded memory and
/// CPU, and a network that can reach five package hosts and nothing else.
/// </para>
/// <para>
/// ⚠️ Unverified in this repository. Nothing in CI runs a container, so what is
/// asserted here is that the right arguments are produced — not that Docker
/// then does what they ask. The distinction matters and is recorded in
/// <c>SPRINT-07_REVIEW.md</c> rather than glossed: Sprint 04 established that a
/// generator's output passing every unit test says nothing about whether the
/// toolchain accepts it, and the same applies to a container runtime.
/// </para>
/// </remarks>
/// <param name="options">Sandbox settings.</param>
public sealed class DockerBuildSandbox(IOptions<SandboxOptions> options) : IBuildSandbox
{
    private readonly SandboxOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public bool IsIsolated => true;

    /// <summary>The container name for a lease, so it can be killed by name.</summary>
    /// <param name="lease">The runner slot.</param>
    /// <returns>The container name.</returns>
    public static string ContainerName(Workflows.RunnerLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return $"shellwright-build-{lease.LeaseId}";
    }

    /// <summary>The docker arguments a build would run with.</summary>
    /// <param name="lease">The runner slot.</param>
    /// <param name="command">The command to run inside the container.</param>
    /// <returns>The full argument list for <c>docker</c>.</returns>
    /// <remarks>
    /// Exposed so the hardening flags can be asserted without a container
    /// runtime. It is not the same as proving Docker honours them, and the
    /// review says so.
    /// </remarks>
    public IReadOnlyList<string> ArgumentsFor(Workflows.RunnerLease lease, SandboxCommand command)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(command);

        var arguments = new List<string>(
            SandboxHardening.RunArguments(
                ContainerName(lease),
                settings.Image,
                lease.WorkspaceRoot,
                lease.CacheRoot,
                settings.Network))
        {
            command.Executable,
        };

        arguments.AddRange(command.Arguments);
        return arguments;
    }

    /// <inheritdoc />
    public Task<Workflows.RunnerLease> PrepareAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);

        var workspace = Path.Combine(settings.WorkspaceRoot, lease.LeaseId);

        // ⚠️ Keyed by app, never shared across tenants. A shared writable
        // Gradle cache is a channel between two customers' builds: one can
        // plant an artefact the other then compiles against.
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

        return await ProcessSandboxRunner.RunAsync(
            "docker",
            ArgumentsFor(lease, command),
            settings.WorkspaceRoot,
            command.Environment,
            onLine,
            onProgress,
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DestroyAsync(Workflows.RunnerLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);

        // Kill first: `--rm` removes the container when it exits, and a build
        // that is still running will not exit on its own.
        await ProcessSandboxRunner.RunAsync(
            "docker",
            ["kill", ContainerName(lease)],
            settings.WorkspaceRoot,
            null,
            (_, _, _) => Task.CompletedTask,
            null,
            TimeSpan.FromSeconds(10),

            // ⚠️ Not the caller's token. This runs on the cancellation path,
            // and a cleanup that is itself cancelled leaves the container alive
            // — which is the exact failure it exists to prevent.
            CancellationToken.None);

        if (Directory.Exists(lease.WorkspaceRoot))
        {
            Directory.Delete(lease.WorkspaceRoot, recursive: true);
        }
    }
}
