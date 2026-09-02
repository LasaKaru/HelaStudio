using System.Text.Json.Nodes;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Patching;

/// <summary>
/// A cached artifact could not be patched, and the build must run in full.
/// </summary>
/// <remarks>
/// ⚠️ A distinct exception rather than a bool, because it is caught and
/// recovered from rather than reported. The cache said the compiled parts had
/// not changed; the artifact says otherwise. Falling through to a full build is
/// always correct — slower, never wrong — and a build must never fail because
/// an optimisation did.
/// </remarks>
public sealed class PatchNotPossibleException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PatchNotPossibleException()
        : base("The cached artifact could not be patched.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the patch could not be applied.</param>
    public PatchNotPossibleException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the patch could not be applied.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public PatchNotPossibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Rebuilds an artifact from a cached one by replacing only its content.
/// </summary>
/// <remarks>
/// ⚠️ Only ever reached on <see cref="CacheOutcome.Patch"/>, which means the
/// code key and the asset key both matched: nothing compiled has changed. What
/// this replaces is one uncompiled JSON file the shell reads at run time. It is
/// not a general "edit the APK" facility, and widening it into one would put
/// this code in the position of hand-editing compiled Android resources, which
/// is where correctness goes to die.
/// </remarks>
public interface IArtifactPatcher
{
    /// <summary>Whether this patcher handles a platform.</summary>
    /// <param name="platform">The platform.</param>
    /// <returns>Whether <see cref="PatchAsync"/> can be called for it.</returns>
    bool Supports(BuildPlatform platform);

    /// <summary>Produces a new artifact from a cached one and a new configuration.</summary>
    /// <param name="request">The build.</param>
    /// <param name="lease">The runner slot, carrying the workspace to work in.</param>
    /// <param name="cached">What the cache offered. Must carry an artifact reference.</param>
    /// <param name="resolvedConfig">The configuration to write into it.</param>
    /// <param name="onLine">Receives progress, so the patch shows up in the build log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The patched artifact.</returns>
    /// <exception cref="PatchNotPossibleException">
    /// The cached artifact is not shaped the way a patch requires. The caller
    /// must run a full build.
    /// </exception>
    Task<BuiltArtifact> PatchAsync(
        BuildRequest request,
        RunnerLease lease,
        CacheLookup cached,
        JsonObject resolvedConfig,
        LogLineHandler onLine,
        CancellationToken cancellationToken = default);
}
