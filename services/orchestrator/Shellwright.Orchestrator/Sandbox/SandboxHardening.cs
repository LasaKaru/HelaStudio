using System.Collections.Immutable;

namespace Shellwright.Orchestrator.Sandbox;

/// <summary>
/// The container flags that make a runner safe to hand a stranger's build.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Written out as data rather than assembled inline, for one reason: a
/// missing flag here has no symptom. A container started without
/// <c>--cap-drop=ALL</c> builds exactly as well as one started with it, and the
/// difference only ever shows up in an incident report. Keeping the set in one
/// place means <c>SandboxHardeningTests</c> can assert on it, and a reviewer
/// can read the whole security posture in twenty lines.
/// </para>
/// <para>
/// Each entry says what it stops, because a flag whose purpose nobody
/// remembers is a flag somebody eventually deletes to fix a build.
/// </para>
/// </remarks>
public static class SandboxHardening
{
    /// <summary>Memory ceiling for one build container.</summary>
    /// <remarks>
    /// Gradle is configured for a 2 GB heap; this leaves headroom for the JVM's
    /// own overhead and stops the container taking the host's Postgres with it.
    /// </remarks>
    public const string MemoryLimit = "3g";

    /// <summary>CPU ceiling for one build container.</summary>
    public const string CpuLimit = "1.5";

    /// <summary>Scratch space, the only writable location.</summary>
    public const string ScratchMount = "/workspace";

    /// <summary>The unprivileged user builds run as.</summary>
    /// <remarks>
    /// A high, fixed id with no matching account in the image, so nothing in
    /// the container maps to a real user on the host if a mount escapes.
    /// </remarks>
    public const string User = "10001:10001";

    /// <summary>Hosts a build is allowed to reach.</summary>
    /// <remarks>
    /// ⚠️ An allowlist, never a blocklist. A build runs code from the
    /// customer's dependency graph — a Gradle plugin, a transitive library —
    /// and any of it can attempt an outbound connection. The question is not
    /// "which hosts are bad" but "which three does a build legitimately need".
    /// </remarks>
    public static ImmutableArray<string> EgressAllowlist { get; } =
    [
        "repo.maven.apache.org",
        "dl.google.com",
        "maven.google.com",
        "plugins.gradle.org",
        "services.gradle.org",
    ];

    /// <summary>Docker arguments that isolate one build.</summary>
    /// <param name="containerName">Name for the container, so it can be killed by name.</param>
    /// <param name="image">The runner image.</param>
    /// <param name="workspaceHostPath">Host directory to mount as scratch.</param>
    /// <param name="cacheHostPath">Host directory holding this app's Gradle cache.</param>
    /// <param name="networkName">The egress-restricted network to attach to.</param>
    /// <returns>Arguments for <c>docker run</c>, before the command itself.</returns>
    public static ImmutableArray<string> RunArguments(
        string containerName,
        string image,
        string workspaceHostPath,
        string cacheHostPath,
        string networkName) =>
    [
        "run",
        "--rm",
        "--name", containerName,

        // Nothing in the image needs to be written. Everything a build writes
        // goes to the scratch mount, which is the whole point of naming one.
        "--read-only",

        // Not root, and unable to become root. `no-new-privileges` is what
        // stops a setuid binary inside the image undoing the user flag.
        "--user", User,
        "--security-opt", "no-new-privileges",

        // A build needs no kernel capabilities whatsoever.
        "--cap-drop", "ALL",

        // ⚠️ Bounded. An unbounded Gradle daemon beside Postgres on a 12 GB
        // host is not a hypothetical: it is the documented way this host dies.
        "--memory", MemoryLimit,
        "--memory-swap", MemoryLimit,
        "--cpus", CpuLimit,
        "--pids-limit", "512",

        // The only writable places.
        "--mount", $"type=bind,source={workspaceHostPath},target={ScratchMount}",
        "--mount", $"type=bind,source={cacheHostPath},target=/home/builder/.gradle",
        "--tmpfs", "/tmp:rw,noexec,nosuid,size=512m",

        // A network with an egress allowlist in front of it. `none` would be
        // safer still and would also mean no build could ever fetch a
        // dependency.
        "--network", networkName,

        "--workdir", ScratchMount,
        image,
    ];
}
