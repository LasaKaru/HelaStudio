using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Shellwright.Codegen.Assets;
using Shellwright.Codegen.Normalisation;
using Shellwright.ConfigSchema;

namespace Shellwright.Codegen.Ios;

/// <summary>
/// Generates <c>Assets.xcassets</c> — the app icon and the themed colour sets.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The app icon is emitted as a <b>single 1024×1024 image</b>, not the
/// fifteen-file set older guides describe. Xcode 14 and later generate every
/// size it needs from one source, which removes fourteen chances to get a
/// dimension wrong and shrinks the asset catalogue accordingly.
/// </para>
/// <para>
/// ⚠️ It is flattened, always. Apple rejects an app icon containing an alpha
/// channel outright, at upload, with an error that names neither the file nor
/// the channel. The schema asks for a source with no transparency for this
/// reason, but a source that has it anyway must be composited rather than
/// passed through — a rejected upload is a worse outcome than a background
/// colour the customer did not pick.
/// </para>
/// <para>
/// Every <c>Contents.json</c> goes through the Sprint 01 canonicaliser, so key
/// order is fixed and the catalogue is byte-identical between runs.
/// </para>
/// </remarks>
public static class IosAssetCatalogue
{
    private const string Root = "Resources/Assets.xcassets";

    /// <summary>The icon a config asks for, or null to keep the shell's placeholder.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <returns>The asset reference, or null.</returns>
    public static string? IconReference(JsonObject resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return resolved["branding"]?["icon"]?.GetValue<string>();
    }

    /// <summary>Renders the whole catalogue for a config.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <param name="assets">Where the source icon is fetched from.</param>
    /// <param name="images">The resizing pipeline.</param>
    /// <returns>The files to add to the project.</returns>
    public static ImmutableArray<GeneratedFile> Render(
        JsonObject resolved,
        IAssetStore assets,
        IImagePipeline images)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(images);

        var files = new List<GeneratedFile>
        {
            Json($"{Root}/Contents.json", CatalogueRoot()),
        };

        files.AddRange(ColourSet("AccentColor", Theme(resolved, "primary"), Theme(resolved, "primary")));
        files.AddRange(ColourSet(
            "SplashBackground",
            Splash(resolved, dark: false),
            Splash(resolved, dark: true)));

        if (IconReference(resolved) is { } reference)
        {
            var background = Rgb.Parse(
                resolved["branding"]?["splash"]?["backgroundColor"]?.GetValue<string>() ?? "#FFFFFF");

            files.Add(new GeneratedFile(
                $"{Root}/AppIcon.appiconset/icon-1024.png",
                [.. images.Render(assets.Read(reference), new IconSpec("", 1024, Flatten: background))]));

            files.Add(Json($"{Root}/AppIcon.appiconset/Contents.json", AppIcon()));
        }

        return [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    private static GeneratedFile Json(string path, JsonObject content) =>
        new(path, [.. TextNormaliser.ToBytes(CanonicalJson.Serialize(content))]);

    private static JsonObject Info() =>
        new() { ["author"] = "shellwright", ["version"] = 1 };

    private static JsonObject CatalogueRoot() => new() { ["info"] = Info() };

    private static JsonObject AppIcon() => new()
    {
        ["images"] = new JsonArray(
            new JsonObject
            {
                ["filename"] = "icon-1024.png",
                ["idiom"] = "universal",
                ["platform"] = "ios",
                ["size"] = "1024x1024",
            }),
        ["info"] = Info(),
    };

    /// <summary>A colour set with explicit light and dark appearances.</summary>
    /// <remarks>
    /// Both are always written, even when identical. A colour set with only a
    /// light appearance does not fall back gracefully — it stays light in dark
    /// mode, which is exactly the "looks like a wrapper" tell the product
    /// exists to avoid.
    /// </remarks>
    private static IEnumerable<GeneratedFile> ColourSet(string name, Rgb light, Rgb dark)
    {
        yield return Json(
            $"{Root}/{name}.colorset/Contents.json",
            new JsonObject
            {
                ["colors"] = new JsonArray(
                    Colour(light, appearance: null),
                    Colour(dark, appearance: "dark")),
                ["info"] = Info(),
            });
    }

    private static JsonObject Colour(Rgb colour, string? appearance)
    {
        var entry = new JsonObject
        {
            ["color"] = new JsonObject
            {
                ["color-space"] = "srgb",
                ["components"] = new JsonObject
                {
                    // Hex strings rather than floats: a float would be written
                    // with whatever precision the formatter chose, and that is
                    // one more thing that could differ between runs.
                    ["alpha"] = "1.000",
                    ["blue"] = $"0x{colour.Blue:X2}",
                    ["green"] = $"0x{colour.Green:X2}",
                    ["red"] = $"0x{colour.Red:X2}",
                },
            },
            ["idiom"] = "universal",
        };

        if (appearance is not null)
        {
            entry["appearances"] = new JsonArray(
                new JsonObject { ["appearance"] = "luminosity", ["value"] = appearance });
        }

        return entry;
    }

    private static Rgb Theme(JsonObject resolved, string key) =>
        Rgb.Parse(resolved["branding"]?["theme"]?[key]?.GetValue<string>() ?? "#2563EB");

    private static Rgb Splash(JsonObject resolved, bool dark)
    {
        var splash = resolved["branding"]?["splash"];

        var hex = dark
            ? splash?["dark"]?["backgroundColor"]?.GetValue<string>()
                ?? splash?["backgroundColor"]?.GetValue<string>()
            : splash?["backgroundColor"]?.GetValue<string>();

        return Rgb.Parse(hex ?? "#FFFFFF");
    }
}
