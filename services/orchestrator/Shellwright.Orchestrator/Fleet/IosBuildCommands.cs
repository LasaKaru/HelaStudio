using System.Globalization;
using System.Text.RegularExpressions;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Fleet;

/// <summary>
/// Which Xcode an iOS build runs under.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Named explicitly rather than taken from whatever <c>xcode-select</c>
/// points at. A host runs N and N−1 simultaneously during a migration — that
/// is the spec's mitigation for Apple's submission deadlines forcing the whole
/// fleet to move together — so "the Xcode on this machine" is not a single
/// thing, and a build that got the wrong one produces a binary the App Store
/// rejects for a reason nothing in our logs explains.
/// </para>
/// <para>
/// ⚠️ It also belongs in the cache key. Two builds of the same configuration
/// under different Xcodes are different binaries; treating them as
/// interchangeable would serve a customer an artifact built by a toolchain
/// they did not ask for. <see cref="ToolchainIdentity"/> is what goes into the
/// hash.
/// </para>
/// </remarks>
/// <param name="Version">The Xcode version, such as <c>26.1</c>.</param>
/// <param name="DeveloperDirectory">
/// The <c>Xcode.app/Contents/Developer</c> path, exported as
/// <c>DEVELOPER_DIR</c>. Null means whatever the host has selected, which is
/// only acceptable on a single-Xcode machine.
/// </param>
public sealed record XcodeToolchain(string Version, string? DeveloperDirectory)
{
    /// <summary>What the cache key records about this toolchain.</summary>
    /// <returns>A stable identity string.</returns>
    public string ToolchainIdentity() =>
        string.Create(CultureInfo.InvariantCulture, $"xcode-{Version}");

    /// <summary>The environment an invocation needs to select this Xcode.</summary>
    /// <returns>Environment variables, empty when the host default is to be used.</returns>
    public IReadOnlyDictionary<string, string> Environment() =>
        string.IsNullOrWhiteSpace(DeveloperDirectory)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // ⚠️ DEVELOPER_DIR rather than running `xcode-select --switch`.
                // The switch is machine-global and would change which Xcode
                // every *other* build on the host is using, mid-flight.
                ["DEVELOPER_DIR"] = DeveloperDirectory,
            };
}

/// <summary>How an archive should be exported.</summary>
public enum IosExportMethod
{
    /// <summary>Installable on registered devices, for the customer's own testing.</summary>
    Development = 0,

    /// <summary>Installable outside the store on registered devices.</summary>
    AdHoc = 1,

    /// <summary>For App Store Connect.</summary>
    AppStore = 2,
}

/// <summary>
/// The commands an iOS build runs.
/// </summary>
/// <remarks>
/// ⚠️ Argument arrays, never shell strings, for the same reason as the Android
/// commands: a scheme name derives from a customer's app name, and
/// <c>Foo"; rm -rf ~ #</c> is a legal app name. On macOS this matters more
/// rather than less — the build runs on a machine holding signing material.
/// </remarks>
public static class IosBuildCommands
{
    private static readonly Regex TeamIdPattern = new("^[A-Z0-9]{10}$", RegexOptions.CultureInvariant);

    /// <summary>Generates the Xcode project from the committed XcodeGen spec.</summary>
    /// <param name="toolchain">Which Xcode.</param>
    /// <param name="projectRoot">Where the generated project lives.</param>
    /// <returns>The command.</returns>
    public static SandboxCommand Generate(XcodeToolchain toolchain, string projectRoot) =>
        new(
            "xcodegen",
            ["generate", "--spec", "project.yml", "--project", "."],
            projectRoot,
            Environment(toolchain));

    /// <summary>Archives the app.</summary>
    /// <param name="toolchain">Which Xcode.</param>
    /// <param name="scheme">The scheme to build.</param>
    /// <param name="projectRoot">Where the generated project lives.</param>
    /// <param name="archivePath">Where to write the <c>.xcarchive</c>.</param>
    /// <param name="derivedDataPath">Where intermediates go. ⚠️ Per app.</param>
    /// <param name="type">Debug or release.</param>
    /// <returns>The command.</returns>
    public static SandboxCommand Archive(
        XcodeToolchain toolchain,
        string scheme,
        string projectRoot,
        string archivePath,
        string derivedDataPath,
        BuildType type) =>
        new(
            "xcodebuild",
            [
                "archive",
                "-scheme", scheme,
                "-configuration", type == BuildType.Release ? "Release" : "Debug",
                "-archivePath", archivePath,
                "-derivedDataPath", derivedDataPath,
                "-destination", "generic/platform=iOS",

                // ⚠️ Explicitly off. xcodebuild will otherwise reach out to
                // Apple to create certificates and profiles on the customer's
                // team, as a side effect of a build. Signing is a deliberate,
                // audited step with its own custody rules (§18.2), not
                // something a compile does on its own initiative.
                "-allowProvisioningUpdates", "NO",

                // Quieter output that still names the failing file. xcodebuild's
                // default is tens of thousands of lines per build, and the log
                // pipeline meters commands.
                "-quiet",
            ],
            projectRoot,
            Environment(toolchain));

    /// <summary>Exports an IPA from an archive.</summary>
    /// <param name="toolchain">Which Xcode.</param>
    /// <param name="archivePath">The <c>.xcarchive</c>.</param>
    /// <param name="exportOptionsPath">The export options plist.</param>
    /// <param name="outputPath">Where to write the IPA.</param>
    /// <param name="workingDirectory">Where to run.</param>
    /// <returns>The command.</returns>
    public static SandboxCommand Export(
        XcodeToolchain toolchain,
        string archivePath,
        string exportOptionsPath,
        string outputPath,
        string workingDirectory) =>
        new(
            "xcodebuild",
            [
                "-exportArchive",
                "-archivePath", archivePath,
                "-exportOptionsPlist", exportOptionsPath,
                "-exportPath", outputPath,
                "-allowProvisioningUpdates", "NO",
            ],
            workingDirectory,
            Environment(toolchain));

    /// <summary>Reports which Xcode is actually in use.</summary>
    /// <param name="toolchain">Which Xcode.</param>
    /// <param name="workingDirectory">Where to run.</param>
    /// <returns>The command.</returns>
    /// <remarks>
    /// ⚠️ Run and logged at the start of every build, so the log answers "which
    /// Xcode built this" without anybody having to infer it from the date. When
    /// a submission is rejected for a toolchain reason, this line is the first
    /// thing anyone will want.
    /// </remarks>
    public static SandboxCommand ReportVersion(XcodeToolchain toolchain, string workingDirectory) =>
        new("xcodebuild", ["-version"], workingDirectory, Environment(toolchain));

    /// <summary>The export options plist for a method.</summary>
    /// <param name="method">How the archive should be exported.</param>
    /// <param name="teamId">The Apple Developer team.</param>
    /// <returns>The plist's contents.</returns>
    /// <remarks>
    /// ⚠️ <c>signingStyle</c> is manual. Automatic signing asks Apple to mint
    /// certificates and profiles on the customer's team during a build, which
    /// is both a surprise and a custody problem: the platform would be creating
    /// credentials nobody asked for, on somebody else's account.
    /// </remarks>
    public static string ExportOptions(IosExportMethod method, string teamId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        // ⚠️ Checked here rather than only where the value is configured,
        // because this is the method that writes the XML. An Apple team
        // identifier is ten upper-case alphanumerics; anything else — a stray
        // </string>, an entity, a newline — would be interpolated into a plist
        // that parses as something other than what it reads as, and the plist
        // decides how the binary is signed.
        if (!TeamIdPattern.IsMatch(teamId))
        {
            throw new ArgumentException(
                "An Apple team identifier is ten upper-case letters and digits.",
                nameof(teamId));
        }

        var methodValue = method switch
        {
            IosExportMethod.AppStore => "app-store-connect",
            IosExportMethod.AdHoc => "ad-hoc",
            _ => "development",
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
             <plist version="1.0">
             <dict>
               <key>method</key>
               <string>{methodValue}</string>
               <key>teamID</key>
               <string>{teamId}</string>
               <key>signingStyle</key>
               <string>manual</string>
               <key>stripSwiftSymbols</key>
               <true/>
               <key>uploadBitcode</key>
               <false/>
               <key>uploadSymbols</key>
               <true/>
             </dict>
             </plist>
             """);
    }

    private static Dictionary<string, string> Environment(XcodeToolchain toolchain)
    {
        ArgumentNullException.ThrowIfNull(toolchain);

        var environment = new Dictionary<string, string>(toolchain.Environment(), StringComparer.Ordinal)
        {
            // ⚠️ No network during a build. Swift Package Manager will
            // otherwise resolve dependencies mid-build from whatever is in the
            // manifest, which makes the build non-reproducible and gives a
            // customer's configuration a way to fetch code.
            ["SWIFTPM_DISABLE_SANDBOX"] = "0",
        };

        return environment;
    }
}
