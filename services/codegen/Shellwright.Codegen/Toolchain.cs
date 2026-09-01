using System.Collections.Immutable;

namespace Shellwright.Codegen;

/// <summary>
/// Everything about the build environment that a generated project depends on
/// but the customer's config does not name.
/// </summary>
/// <remarks>
/// This is part of the code cache key (ADR 0004). Bumping the Android Gradle
/// Plugin has to invalidate every cached build, and it can only do that if the
/// version is an explicit input rather than something the runner happens to
/// have installed.
/// </remarks>
/// <param name="ShellVersion">Semver of the shell template being rendered.</param>
/// <param name="GeneratorVersion">Semver of this generator.</param>
/// <param name="Versions">Named tool versions, such as <c>agp</c> and <c>kotlin</c>.</param>
public sealed record ToolchainDescriptor(
    string ShellVersion,
    string GeneratorVersion,
    ImmutableSortedDictionary<string, string> Versions)
{
    /// <summary>The toolchain the in-tree shell is pinned to.</summary>
    /// <remarks>
    /// ⚠️ These must match <c>shells/android/gradle/libs.versions.toml</c>.
    /// <c>ToolchainDescriptorTests</c> asserts it by reading that file, because
    /// a descriptor that has drifted from the versions actually used produces
    /// cache keys that claim two different builds are the same.
    /// </remarks>
    public static ToolchainDescriptor Android { get; } = new(
        ShellVersion: "0.3.0",
        GeneratorVersion: "0.4.0",
        Versions: ImmutableSortedDictionary.CreateRange(
            StringComparer.Ordinal,
            [
                new KeyValuePair<string, string>("agp", "8.9.0"),
                new KeyValuePair<string, string>("kotlin", "2.1.0"),
                new KeyValuePair<string, string>("compileSdk", "36"),
            ]));

    /// <summary>The toolchain the in-tree iOS shell is pinned to.</summary>
    /// <remarks>
    /// ⚠️ <c>xcodegen</c> is here because it decides the bytes of the generated
    /// project. XcodeGen derives its object UUIDs from paths and names, which
    /// makes it deterministic in principle — but a version bump can change that
    /// derivation, and a change in project bytes must invalidate the build
    /// cache deliberately rather than surface as a mysterious full rebuild.
    ///
    /// <c>xcode</c> is here for the same reason and a sharper one: Xcode's
    /// project format shifts roughly annually, and that break is certain rather
    /// than hypothetical.
    /// </remarks>
    public static ToolchainDescriptor Ios { get; } = new(
        ShellVersion: "0.3.0",
        GeneratorVersion: "0.5.0",
        Versions: ImmutableSortedDictionary.CreateRange(
            StringComparer.Ordinal,
            [
                new KeyValuePair<string, string>("xcode", "16.2"),
                new KeyValuePair<string, string>("xcodegen", "2.42.0"),
                new KeyValuePair<string, string>("swift", "6.0"),
                new KeyValuePair<string, string>("iosDeploymentTarget", "15.0"),
            ]));

    /// <summary>The toolchain as hash context, for the three cache keys.</summary>
    /// <returns>A hash context naming this shell and toolchain.</returns>
    public ConfigSchema.HashContext ToHashContext() =>
        new(ShellVersion, PluginLock: null, Toolchain: Versions);
}
