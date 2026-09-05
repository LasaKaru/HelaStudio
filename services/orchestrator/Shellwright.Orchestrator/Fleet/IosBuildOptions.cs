using System.ComponentModel.DataAnnotations;
using Shellwright.Codegen;

namespace Shellwright.Orchestrator.Fleet;

/// <summary>
/// What this deployment's macOS fleet can build with.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Deployment configuration rather than customer configuration, and the
/// distinction matters. An Apple team id identifies who signs a binary, and
/// letting a config document name one would mean a customer could ask the
/// platform to sign on somebody else's team. Customer-supplied signing
/// material has its own custody rules and its own sprint (§18.2); until then
/// iOS builds are development-signed by the platform's own team, named here.
/// </para>
/// <para>
/// ⚠️ <see cref="XcodeVersion"/> is the single source for both the
/// <c>DEVELOPER_DIR</c> that selects a toolchain and the <c>xcode</c> entry in
/// the build cache key. They cannot be configured apart, because a deployment
/// that built with one Xcode and keyed the cache with another would serve
/// artifacts produced by a toolchain nobody asked for — the exact failure
/// <see cref="XcodeToolchain"/> exists to prevent.
/// </para>
/// </remarks>
public sealed class IosBuildOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Ios";

    /// <summary>The Xcode the fleet builds with, such as <c>16.2</c>.</summary>
    /// <remarks>
    /// Defaults to the version the in-tree iOS shell is pinned to, so a
    /// deployment that names nothing still keys its cache against a real
    /// toolchain rather than against an empty string.
    /// </remarks>
    [Required]
    [RegularExpression(@"^\d+(\.\d+){0,2}$")]
    public string XcodeVersion { get; set; } = DefaultXcodeVersion;

    /// <summary>
    /// The <c>Xcode.app/Contents/Developer</c> path that <see cref="XcodeVersion"/> lives at.
    /// </summary>
    /// <remarks>
    /// Null is acceptable only on a host with one Xcode installed. A host
    /// running N and N−1 during a migration must name the directory, or every
    /// build takes whatever <c>xcode-select</c> last pointed at.
    /// </remarks>
    public string? DeveloperDirectory { get; set; }

    /// <summary>The Apple Developer team an export is signed for.</summary>
    /// <remarks>
    /// ⚠️ Constrained to Apple's ten-character team identifier. The value is
    /// interpolated into an XML plist, so the pattern is what keeps a
    /// misconfigured deployment from producing a plist that means something
    /// other than what it reads as.
    /// </remarks>
    [RegularExpression("^[A-Z0-9]{10}$")]
    public string? TeamId { get; set; }

    /// <summary>How archives are exported.</summary>
    public IosExportMethod ExportMethod { get; set; } = IosExportMethod.Development;

    /// <summary>The Xcode version the in-tree iOS shell is pinned to.</summary>
    public static string DefaultXcodeVersion => ToolchainDescriptor.Ios.Versions["xcode"];

    /// <summary>Whether this deployment can actually export an iOS build.</summary>
    /// <remarks>
    /// Without a team id there is nothing to sign for, and an archive that
    /// cannot be exported is a twenty-minute build that produces nothing. The
    /// build refuses up front instead.
    /// </remarks>
    public bool CanExport => !string.IsNullOrWhiteSpace(TeamId);

    /// <summary>The toolchain these settings select.</summary>
    /// <returns>The toolchain, for both the commands and the cache key.</returns>
    public XcodeToolchain Toolchain() => new(XcodeVersion, DeveloperDirectory);
}
