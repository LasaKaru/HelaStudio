using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Shellwright.ConfigSchema;

namespace Shellwright.Api.Config;

/// <summary>Facts about the build system that feed the cache key.</summary>
/// <remarks>
/// ⚠️ These are inputs to <c>codeKey</c>, so changing one invalidates every
/// cached native build. That is the intent — a new AGP or Xcode must not reuse
/// artefacts produced by the old one — but it means these values belong in
/// deployment configuration, reviewed like a dependency bump, rather than in a
/// constant somebody edits in passing.
/// </remarks>
public sealed class BuildContextOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Build";

    /// <summary>Semver of the shell template apps are built from.</summary>
    [Required]
    public string ShellVersion { get; set; } = "1.0.0";

    /// <summary>Toolchain identity, such as AGP and Xcode versions.</summary>
    public Dictionary<string, string> Toolchain { get; } = [];
}

/// <summary>Supplies the hashing context for the current deployment.</summary>
/// <param name="options">Build settings.</param>
public sealed class HashContextProvider(IOptions<BuildContextOptions> options)
{
    private readonly BuildContextOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Builds the context the three cache keys are computed against.</summary>
    /// <param name="pluginLock">Resolved plugin versions, when known.</param>
    /// <returns>The context.</returns>
    public HashContext Create(IReadOnlyDictionary<string, string>? pluginLock = null) =>
        new(settings.ShellVersion, pluginLock, settings.Toolchain);
}
