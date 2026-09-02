using System.Text.Json.Nodes;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Activities;

/// <summary>What to generate, and where.</summary>
/// <param name="Config">The resolved configuration.</param>
/// <param name="Platform">Which platform's project.</param>
/// <param name="WorkspaceRoot">Where to write it.</param>
/// <param name="Hashes">The cache keys, recorded into the generated manifest.</param>
public sealed record GenerationRequest(
    JsonObject Config,
    BuildPlatform Platform,
    string WorkspaceRoot,
    BuildHashes Hashes);

/// <summary>Turns a configuration into a buildable project.</summary>
public interface IProjectGenerator
{
    /// <summary>Generates the project.</summary>
    /// <param name="request">What to generate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What was written.</returns>
    Task<GeneratedProject> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Whether an artifact may ship.</summary>
/// <param name="Accepted">True when every check passed.</param>
/// <param name="Reason">Why it did not, when it did not.</param>
public sealed record VerificationVerdict(bool Accepted, string Reason)
{
    /// <summary>Every check passed.</summary>
    public static VerificationVerdict Ok { get; } = new(true, string.Empty);

    /// <summary>A check failed.</summary>
    /// <param name="reason">What a person can do about it.</param>
    /// <returns>The verdict.</returns>
    public static VerificationVerdict Rejected(string reason) => new(false, reason);
}

/// <summary>Checks what the toolchain produced before anybody can download it.</summary>
public interface IArtifactVerifier
{
    /// <summary>Checks one artifact.</summary>
    /// <param name="request">The build it came from.</param>
    /// <param name="artifactPath">Where it is.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether it may ship.</returns>
    Task<VerificationVerdict> VerifyAsync(
        BuildRequest request,
        string artifactPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Stores finished artifacts.</summary>
public interface IArtifactStore
{
    /// <summary>Stores an artifact, addressed by its own hash.</summary>
    /// <param name="request">The build it came from.</param>
    /// <param name="artifactPath">Where it is on the runner.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Where it ended up.</returns>
    Task<UploadedArtifact> StoreAsync(
        BuildRequest request,
        string artifactPath,
        CancellationToken cancellationToken = default);

    /// <summary>Copies a stored artifact back onto a runner.</summary>
    /// <param name="artifactReference">What <see cref="StoreAsync"/> returned.</param>
    /// <param name="destinationPath">Where to put it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many bytes were written.</returns>
    /// <remarks>
    /// ⚠️ To a path, not to a byte array. A release APK is tens of megabytes
    /// and several builds run at once; returning one as a <c>byte[]</c> is a
    /// managed allocation on the large object heap per concurrent build, which
    /// is how the orchestrator is killed by the OOM killer.
    /// </remarks>
    Task<long> FetchAsync(
        string artifactReference,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Streams build output live and archives it durably.</summary>
public interface IBuildLogPipeline
{
    /// <summary>Appends one line.</summary>
    /// <param name="buildId">Which build.</param>
    /// <param name="line">The line, before redaction.</param>
    /// <param name="isError">Whether it came from standard error.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the line has been accepted.</returns>
    Task AppendAsync(Guid buildId, string line, bool isError, CancellationToken cancellationToken = default);

    /// <summary>Finishes the durable record.</summary>
    /// <param name="buildId">Which build.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the archive is closed.</returns>
    Task ArchiveAsync(Guid buildId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The commands each platform's build runs.
/// </summary>
/// <remarks>
/// ⚠️ Kept in one place, and always as argument arrays. The values that reach a
/// build command come from a customer's configuration — an app name, a version
/// string — and an app named <c>Foo"; rm -rf / #</c> is a legal app name.
/// Building the command line as a string anywhere would make that a shell
/// injection with a REST endpoint in front of it.
/// </remarks>
public static class BuildCommands
{
    /// <summary>The command that builds a generated project.</summary>
    /// <param name="request">The build.</param>
    /// <param name="project">The generated project.</param>
    /// <returns>The command to run.</returns>
    public static SandboxCommand For(BuildRequest request, GeneratedProject project)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);

        if (request.Platform != BuildPlatform.Android)
        {
            throw new NotSupportedException(
                "iOS builds need a macOS runner, which arrives in Sprint 08.");
        }

        var task = request.Type == BuildType.Release ? "assembleRelease" : "assembleDebug";

        return new SandboxCommand(
            "./gradlew",
            [
                task,
                "--console=plain",

                // ⚠️ No daemon. The container is destroyed after one build, so a
                // daemon has nothing to be warm for and everything to leak into:
                // it outlives the build, holds the workspace open, and is the
                // classic reason a "finished" build keeps its memory.
                "--no-daemon",
                "--stacktrace",
            ],
            project.ProjectRoot,
            new Dictionary<string, string>
            {
                // Bounded explicitly. An unbounded Gradle JVM next to Postgres
                // on a 12 GB host takes the whole box down.
                ["GRADLE_OPTS"] = "-Xmx2g -XX:MaxMetaspaceSize=512m",
                ["GRADLE_USER_HOME"] = Path.Combine(project.ProjectRoot, ".gradle"),
            });
    }

    /// <summary>Where the build leaves its artifact.</summary>
    /// <param name="request">The build.</param>
    /// <param name="project">The generated project.</param>
    /// <returns>The path to look in.</returns>
    public static string ArtifactPath(BuildRequest request, GeneratedProject project)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(project);

        var flavour = request.Type == BuildType.Release ? "release" : "debug";

        return Path.Combine(project.ProjectRoot, "app", "build", "outputs", "apk", flavour);
    }
}
