using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Shellwright.Codegen.Assets;
using Shellwright.Codegen.Templating;

namespace Shellwright.Codegen.Ios;

/// <summary>
/// Renders <c>shells/ios</c> into a buildable Xcode project.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The project file itself is <b>not</b> generated. <c>project.pbxproj</c>
/// keys its objects by 96-bit UUIDs and has no stable public format; templating
/// it produces something unmaintainable that breaks whenever Xcode's format
/// shifts. Instead this emits a 60-line <c>project.yml</c> and XcodeGen turns
/// that into the project on the Mac — a readable input we control, rather than
/// a plist we would be guessing at.
/// </para>
/// <para>
/// The consequence is that determinism here is a property of
/// <c>project.yml</c>, which this generator owns, plus XcodeGen's own
/// derivation, which it does not. XcodeGen derives its UUIDs from paths and
/// names, so it is deterministic in principle — but the version is pinned in
/// the toolchain descriptor and the guarantee is asserted on the Mac rather
/// than assumed here.
/// </para>
/// </remarks>
public sealed class IosProjectGenerator : ProjectGenerator
{
    /// <summary>Creates a generator reading from the iOS shell.</summary>
    /// <param name="templates">The shell to render.</param>
    /// <param name="assets">Where <c>asset://</c> references are resolved.</param>
    /// <param name="images">The icon-resizing pipeline.</param>
    /// <param name="includeTests">
    /// Whether to keep the shell's own test target. True only when rendering
    /// the shell back over itself; a customer's project never gets it.
    /// </param>
    public IosProjectGenerator(
        TemplateSource templates,
        IAssetStore assets,
        IImagePipeline? images = null,
        bool includeTests = false)
        : base(templates, assets, images) => this.includeTests = includeTests;

    private readonly bool includeTests;

    /// <inheritdoc/>
    protected override string Platform => "ios";

    /// <inheritdoc/>
    protected override string ConfigAssetPath => "Resources/appconfig.json";

    /// <inheritdoc/>
    protected override ImmutableArray<(string Suffix, TemplateFormat Format)> FormatsByPath =>
        [
            (".yml", TemplateFormat.Yaml),
            (".swift", TemplateFormat.Swift),
            (".plist", TemplateFormat.Xml),
            (".entitlements", TemplateFormat.Xml),
            (".xcprivacy", TemplateFormat.Xml),
            (".json", TemplateFormat.Json),
            (".sh", TemplateFormat.None),
            (".md", TemplateFormat.None),
        ];

    /// <inheritdoc/>
    protected override ImmutableArray<string> ExcludedFromGenerated =>
        includeTests ? [] : ["Tests/"];

    /// <inheritdoc/>
    protected override ImmutableArray<GeneratedFile> PlatformFiles(JsonObject resolved) =>
        IosAssetCatalogue.Render(resolved, Assets, Images);

    /// <inheritdoc/>
    protected override IReadOnlyDictionary<string, object?> ExtraValues(
        JsonObject resolved,
        ToolchainDescriptor toolchain) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["usageDescriptions"] = UsageDescriptions(resolved),
            ["orientations"] = Orientations(resolved),
            ["associatedDomains"] = AssociatedDomains(resolved),
            ["customScheme"] = resolved["deepLinks"]?["customScheme"]?.GetValue<string>() ?? string.Empty,
            ["hasGeneratedIcon"] = IosAssetCatalogue.IconReference(resolved) is not null,
            ["projectName"] = ProjectName(resolved),

            // ⚠️ False for a generated project, and it has to be: dropping
            // Tests/ without also dropping the target leaves SwiftPM pointing at
            // a directory that does not exist, and `swift build` fails before it
            // compiles a line. The shell keeps both.
            ["includeTests"] = includeTests,
        };

    /// <summary>
    /// An Xcode-safe project name.
    /// </summary>
    /// <remarks>
    /// Xcode puts the project name in file paths and scheme names, so the same
    /// slugging Gradle needs applies here. Underscores rather than hyphens:
    /// a hyphen is legal but reads badly in a scheme selector, and the name is
    /// visible to anyone who opens the exported project.
    /// </remarks>
    private static string ProjectName(JsonObject resolved) => Slug(resolved, '_');

    /// <summary>
    /// The <c>NS…UsageDescription</c> keys this config needs, sorted.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the single most consequential mapping in iOS generation. An
    /// app that reaches a capability with no usage string does not degrade or
    /// warn — it is killed by the system, instantly, the first time a web form
    /// asks for the camera. The customer sees a crash on a device and has no
    /// way to connect it to a missing plist key.
    ///
    /// The mirror-image failure is a rejection: Apple's static analysis flags a
    /// usage string for a capability the binary cannot reach, so an unjustified
    /// string is as harmful as a missing one. Both directions are why this is
    /// derived from the config rather than left permanently in the template.
    /// </remarks>
    public static ImmutableSortedDictionary<string, string> UsageDescriptions(JsonObject resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var permissions = resolved["permissions"] as JsonObject;
        var strings = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        if (permissions?["camera"]?.GetValue<bool>() == true)
        {
            strings["NSCameraUsageDescription"] = "Take photos to upload.";
        }

        if (permissions?["microphone"]?.GetValue<bool>() == true)
        {
            strings["NSMicrophoneUsageDescription"] = "Record audio to upload.";
        }

        if (permissions?["photoLibrary"]?.GetValue<bool>() == true)
        {
            strings["NSPhotoLibraryUsageDescription"] = "Choose photos to upload.";
        }

        switch (permissions?["location"]?.GetValue<string>())
        {
            case "whenInUse":
                strings["NSLocationWhenInUseUsageDescription"] =
                    "Show you content relevant to where you are.";
                break;

            case "always":
                // ⚠️ Both keys. iOS requires the when-in-use string even for an
                // always-authorisation request, and asking for Always without
                // it produces a dialog the user cannot grant.
                strings["NSLocationWhenInUseUsageDescription"] =
                    "Show you content relevant to where you are.";
                strings["NSLocationAlwaysAndWhenInUseUsageDescription"] =
                    "Show you content relevant to where you are, including in the background.";
                break;

            default:
                break;
        }

        return strings.ToImmutable();
    }

    /// <summary>
    /// The supported interface orientations.
    /// </summary>
    /// <remarks>
    /// ⚠️ iPad is judged separately. An app that does not support all four
    /// orientations on iPad is rejected unless the restriction is justified,
    /// and the same app may legitimately be portrait-only on iPhone — so a
    /// single list for both is wrong in one direction or the other. The iPad
    /// list is emitted under its own key.
    /// </remarks>
    public static ImmutableArray<string> Orientations(JsonObject resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        return resolved["build"]?["orientation"]?.GetValue<string>() switch
        {
            "portrait" => ["UIInterfaceOrientationPortrait"],
            "landscape" => ["UIInterfaceOrientationLandscapeLeft", "UIInterfaceOrientationLandscapeRight"],
            _ =>
            [
                "UIInterfaceOrientationPortrait",
                "UIInterfaceOrientationLandscapeLeft",
                "UIInterfaceOrientationLandscapeRight",
            ],
        };
    }

    /// <summary>
    /// Associated-domain entitlements for Universal Links, sorted.
    /// </summary>
    /// <remarks>
    /// ⚠️ Each needs an <c>apple-app-site-association</c> file served over HTTPS
    /// from the domain, with no redirects. Without it iOS silently declines to
    /// open links in the app, which reads to a customer as "deep links do not
    /// work" with nothing in any log to explain it.
    /// </remarks>
    public static ImmutableArray<string> AssociatedDomains(JsonObject resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var hosts = new SortedSet<string>(StringComparer.Ordinal);

        if (resolved["deepLinks"]?["universalLinks"] is JsonArray links)
        {
            foreach (var link in links)
            {
                if (link is JsonValue value && value.TryGetValue<string>(out var host) && host.Length > 0)
                {
                    hosts.Add($"applinks:{host}");
                }
            }
        }

        return [.. hosts];
    }
}
