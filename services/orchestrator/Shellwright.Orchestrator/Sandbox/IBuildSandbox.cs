using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Sandbox;

/// <summary>One command to run inside a sandbox.</summary>
/// <param name="Executable">The program.</param>
/// <param name="Arguments">
/// Arguments, one element per argument.
/// </param>
/// <param name="WorkingDirectory">Where to run it.</param>
/// <param name="Environment">Extra environment variables.</param>
/// <remarks>
/// ⚠️ An array, never a command line to be parsed by a shell.
///
/// The generated project's Gradle invocation carries values the customer chose:
/// an app name, a bundle id, a version string. An app named
/// <c>Foo"; rm -rf / #</c> is a perfectly legal app name and a catastrophic
/// shell fragment. Passing arguments as an array means no shell ever sees them,
/// and <c>TC-S07-BLD-026</c> builds exactly that app to prove it.
/// </remarks>
public sealed record SandboxCommand(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>How a sandboxed command ended.</summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="Duration">How long it took, for metering.</param>
public sealed record SandboxResult(int ExitCode, TimeSpan Duration);

/// <summary>Receives a line of output as it is produced.</summary>
/// <param name="line">The line, already framed.</param>
/// <param name="isError">Whether it came from standard error.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>A task that completes when the line has been handled.</returns>
public delegate Task LogLineHandler(string line, bool isError, CancellationToken cancellationToken);

/// <summary>
/// Runs build commands in an isolated environment.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists because there are two implementations with very
/// different security properties, and the difference must be visible in the
/// type system rather than in a comment: <see cref="DockerBuildSandbox"/>
/// isolates, and <see cref="LocalBuildSandbox"/> does not.
/// </para>
/// <para>
/// ⚠️ Only the Docker implementation is fit to run customer configurations.
/// The local one exists so the build activities can be developed and tested
/// where no container runtime is available, and it refuses to start unless it
/// is explicitly allowed — see its own documentation.
/// </para>
/// </remarks>
public interface IBuildSandbox
{
    /// <summary>Whether this implementation actually isolates the build from the host.</summary>
    bool IsIsolated { get; }

    /// <summary>Prepares a workspace for one build.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot it holds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lease, with its workspace and cache paths filled in.</returns>
    Task<Workflows.RunnerLease> PrepareAsync(
        BuildRequest request,
        Workflows.RunnerLease lease,
        CancellationToken cancellationToken = default);

    /// <summary>Runs one command, streaming its output.</summary>
    /// <param name="lease">The runner slot, carrying the workspace to run in.</param>
    /// <param name="command">What to run.</param>
    /// <param name="onLine">Receives each line as it is produced.</param>
    /// <param name="onProgress">Called periodically so the caller can heartbeat.</param>
    /// <param name="cancellationToken">Cancellation token. ⚠️ Must kill the process, not just stop reading.</param>
    /// <returns>How it ended.</returns>
    Task<SandboxResult> RunAsync(
        Workflows.RunnerLease lease,
        SandboxCommand command,
        LogLineHandler onLine,
        Action? onProgress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Destroys the workspace and everything in it.</summary>
    /// <param name="lease">The runner slot, carrying the workspace to destroy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once nothing of the build remains.</returns>
    /// <remarks>
    /// ⚠️ Always called, including on the failure and cancellation paths. A
    /// workspace that survives a build is a workspace the next tenant's build
    /// might see.
    /// </remarks>
    Task DestroyAsync(Workflows.RunnerLease lease, CancellationToken cancellationToken = default);
}
