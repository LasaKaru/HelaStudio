using Microsoft.Extensions.Options;
using Shellwright.Codegen;
using Shellwright.ConfigSchema;
using Shellwright.Orchestrator.Fleet;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Activities;

/// <summary>
/// Which toolchain each platform builds with, and therefore what its cache key
/// has to say.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Per platform, and this is a correctness requirement rather than tidiness.
/// The orchestrator previously computed its cache keys against a single
/// injected <see cref="HashContext"/> that nothing registered and that named no
/// toolchain at all. The consequence is the one ADR 0004 exists to prevent: a
/// bump to AGP, Kotlin or Xcode would not have changed a single cache key, so
/// every app would have gone on being served artifacts compiled by the previous
/// toolchain until something else in its config happened to change.
/// </para>
/// <para>
/// ⚠️ The descriptors are the same ones the generator renders from
/// (<see cref="ToolchainDescriptor"/>), so the key the orchestrator computes is
/// the key the generated project was actually produced under. Two sources for
/// this would be two answers to "what built this".
/// </para>
/// </remarks>
/// <param name="options">This deployment's iOS settings.</param>
public sealed class BuildToolchains(IOptions<IosBuildOptions> options)
{
    private readonly IosBuildOptions ios = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The toolchain a platform builds with.</summary>
    /// <param name="platform">Which platform.</param>
    /// <returns>Its descriptor.</returns>
    /// <exception cref="NotSupportedException">No toolchain is pinned for that platform.</exception>
    public ToolchainDescriptor For(BuildPlatform platform) => platform switch
    {
        BuildPlatform.Android => ToolchainDescriptor.Android,

        // ⚠️ The pinned descriptor with the deployment's Xcode substituted in,
        // rather than the descriptor as written. The fleet builds with whatever
        // IosBuildOptions selects; if the key said 16.2 while the hosts ran
        // 26.1, the cache would hand out binaries from a toolchain nobody
        // asked for. One value, two uses — see IosBuildOptions.XcodeVersion.
        BuildPlatform.Ios => ToolchainDescriptor.Ios with
        {
            Versions = ToolchainDescriptor.Ios.Versions.SetItem("xcode", ios.XcodeVersion),
        },
        _ => throw new NotSupportedException($"No toolchain is pinned for {platform}."),
    };

    /// <summary>The context a platform's cache keys are computed against.</summary>
    /// <param name="platform">Which platform.</param>
    /// <returns>The hash context.</returns>
    public HashContext HashContextFor(BuildPlatform platform) => For(platform).ToHashContext();
}
