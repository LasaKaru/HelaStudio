using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Shellwright.Codegen.Assets;
using Shellwright.Codegen.Templating;

namespace Shellwright.Codegen.Android;

/// <summary>
/// Renders <c>shells/android</c> into a buildable Gradle project.
/// </summary>
/// <remarks>
/// The pipeline itself — rendering, normalising, hashing, the manifest — lives
/// in <see cref="ProjectGenerator"/>, shared with iOS. Only what is genuinely
/// Android is here.
/// </remarks>
public sealed class AndroidProjectGenerator : ProjectGenerator
{
    /// <summary>Creates a generator reading from the Android shell.</summary>
    /// <param name="templates">The shell to render.</param>
    /// <param name="assets">Where <c>asset://</c> references are resolved.</param>
    /// <param name="images">The icon-resizing pipeline.</param>
    public AndroidProjectGenerator(
        TemplateSource templates,
        IAssetStore assets,
        IImagePipeline? images = null)
        : base(templates, assets, images)
    {
    }

    /// <inheritdoc/>
    protected override string Platform => "android";

    /// <inheritdoc/>
    protected override string ConfigAssetPath => "app/src/main/assets/appconfig.json";

    /// <inheritdoc/>
    protected override ImmutableArray<(string Suffix, TemplateFormat Format)> FormatsByPath =>
        [
            ("strings.xml", TemplateFormat.AndroidResource),
            (".xml", TemplateFormat.Xml),
            (".kts", TemplateFormat.GradleKotlin),
            (".gradle", TemplateFormat.GradleKotlin),
            (".json", TemplateFormat.Json),
            (".pro", TemplateFormat.None),
            (".toml", TemplateFormat.None),
            (".properties", TemplateFormat.None),
        ];

    /// <summary>
    /// The placeholder vector a generated icon makes redundant.
    /// </summary>
    /// <remarks>
    /// It exists so the shell builds standalone. Once the config supplies a
    /// real icon it is unreferenced, and lint fails the customer's build over
    /// the unused resource — so it is dropped rather than shipped as dead
    /// weight.
    /// </remarks>
    protected override ImmutableArray<string> Superseded(JsonObject resolved) =>
        AndroidIcons.IconReference(resolved) is null
            ? []
            : ["app/src/main/res/drawable/ic_launcher_foreground.xml"];

    /// <inheritdoc/>
    protected override ImmutableArray<GeneratedFile> PlatformFiles(JsonObject resolved) =>
        AndroidIcons.Render(resolved, Assets, Images);

    /// <inheritdoc/>
    protected override IReadOnlyDictionary<string, object?> ExtraValues(
        JsonObject resolved,
        ToolchainDescriptor toolchain) =>
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["locales"] = Locales(resolved),
            ["deepLinkHosts"] = DeepLinkHosts(resolved),
            ["grantedPermissions"] = GrantedPermissions(resolved),
            ["removedPermissions"] = RemovedPermissions(resolved),
            ["projectSlug"] = Slug(resolved),
            ["screenOrientation"] = ScreenOrientation(resolved),
            ["customScheme"] = resolved["deepLinks"]?["customScheme"]?.GetValue<string>() ?? string.Empty,

            // Whether the launcher icon was generated from the config or is
            // still the shell's placeholder. The adaptive-icon XML has to
            // point at whichever exists, or a customer's app ships showing
            // the placeholder on every Android 8 and later device.
            ["hasGeneratedIcon"] = AndroidIcons.IconReference(resolved) is not null,
        };

    /// <summary>The locales the app ships resources for, always including one.</summary>
    private static List<string> Locales(JsonObject resolved)
    {
        var locales = (resolved["localization"]?["locales"] as JsonArray)
            ?.Select(node => node!.GetValue<string>())
            .ToList() ?? [];

        if (locales.Count == 0)
        {
            locales.Add("en");
        }

        // Sorted so resourceConfigurations is stable whatever order the
        // customer listed their languages in. Unsorted, a mere reorder would
        // read to the cache as a code change and force a full recompile.
        locales.Sort(StringComparer.Ordinal);
        return locales;
    }

    /// <summary>App Links hosts, sorted and deduplicated.</summary>
    private static List<string> DeepLinkHosts(JsonObject resolved)
    {
        var hosts = new SortedSet<string>(StringComparer.Ordinal);

        if (resolved["deepLinks"]?["universalLinks"] is JsonArray links)
        {
            // The schema types these as bare hostname strings, not objects.
            foreach (var link in links)
            {
                if (link is JsonValue value
                    && value.TryGetValue<string>(out var host)
                    && host.Length > 0)
                {
                    hosts.Add(host);
                }
            }
        }

        return [.. hosts];
    }

    /// <summary>
    /// Android permission names the config actually justifies.
    /// </summary>
    /// <remarks>
    /// Everything not listed is stripped with <c>tools:node="remove"</c>. An
    /// unjustified permission is among the most common store rejection reasons,
    /// which is why Sprint 01 warns about it (<c>CFG_PERMISSION_UNJUSTIFIED</c>)
    /// and why the generator <i>removes</i> rather than merely omits: a library
    /// that declares one on the app's behalf must not reintroduce it during
    /// manifest merging.
    /// </remarks>
    private static readonly ImmutableArray<string> OptionalPermissions =
        [
            "android.permission.ACCESS_COARSE_LOCATION",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.CAMERA",
            "android.permission.POST_NOTIFICATIONS",
            "android.permission.RECORD_AUDIO",
        ];

    /// <summary>Optional permissions the config does not justify, sorted.</summary>
    private static List<string> RemovedPermissions(JsonObject resolved)
    {
        var granted = GrantedPermissions(resolved);
        return [.. OptionalPermissions.Where(name => !granted.Contains(name))];
    }

    /// <summary>
    /// The <c>android:screenOrientation</c> value, or empty to omit the attribute.
    /// </summary>
    /// <remarks>
    /// ⚠️ Empty rather than <c>"unspecified"</c> for the "any" case, and the
    /// template omits the attribute entirely. They behave identically at
    /// runtime, but Android lint flags <i>any</i> use of the attribute
    /// (<c>DiscouragedApi</c>) because a fixed orientation letterboxes the app
    /// on tablets and foldables — and lint runs with warnings as errors in
    /// every generated project, so emitting a redundant attribute would fail a
    /// customer's build for nothing.
    ///
    /// When a customer does ask for a fixed orientation, the template suppresses
    /// the check on that one attribute. Their explicit choice is not something
    /// their build should refuse.
    /// </remarks>
    private static string ScreenOrientation(JsonObject resolved) =>
        resolved["build"]?["orientation"]?.GetValue<string>() switch
        {
            "portrait" => "portrait",
            "landscape" => "landscape",
            _ => string.Empty,
        };

    private static List<string> GrantedPermissions(JsonObject resolved)
    {
        var permissions = resolved["permissions"] as JsonObject;
        var granted = new SortedSet<string>(StringComparer.Ordinal);

        if (permissions?["camera"]?.GetValue<bool>() == true)
        {
            granted.Add("android.permission.CAMERA");
        }

        if (permissions?["microphone"]?.GetValue<bool>() == true)
        {
            granted.Add("android.permission.RECORD_AUDIO");
        }

        if (permissions?["notifications"]?.GetValue<bool>() == true)
        {
            granted.Add("android.permission.POST_NOTIFICATIONS");
        }

        if (permissions?["location"]?.GetValue<string>() is "whenInUse" or "always")
        {
            // ⚠️ Both, always. Since Android 12 a request for FINE without
            // COARSE is rejected at the permission dialog: the user is offered
            // an "approximate location" choice that the app has not declared,
            // so the grant silently fails. Lint calls this out
            // (CoarseFineLocation) but the runtime consequence is worse than a
            // lint error — location simply never works for that customer.
            granted.Add("android.permission.ACCESS_COARSE_LOCATION");
            granted.Add("android.permission.ACCESS_FINE_LOCATION");
        }

        return [.. granted];
    }

}
