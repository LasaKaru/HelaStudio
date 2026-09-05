using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Shellwright.Codegen.Assets;

namespace Shellwright.Codegen.Android;

/// <summary>
/// Generates the Android launcher icon set from one source image.
/// </summary>
/// <remarks>
/// <para>
/// Android wants the same icon at five densities twice over — once as a legacy
/// square bitmap, once as the foreground layer of an adaptive icon — plus a
/// monochrome layer for Android 13 themed icons. Nine files from one upload.
/// </para>
/// <para>
/// ⚠️ The adaptive-icon geometry is the part that goes wrong silently. The
/// foreground layer is 108dp, but launchers mask it to a shape and may animate
/// it, so only the central 72dp is guaranteed visible and only the central 66dp
/// is safe on a circular mask. Scaling a logo to fill 108dp puts a third of it
/// outside the mask, and the result is a clipped icon on exactly the devices
/// the customer's users have. The source is therefore drawn at the safe size
/// and padded out, not scaled to the full layer.
/// </para>
/// </remarks>
public static class AndroidIcons
{
    /// <summary>Launcher icon sizes in pixels, by density bucket.</summary>
    /// <remarks>48dp at 1x, so mdpi is 48 and xxxhdpi is 192.</remarks>
    private static readonly ImmutableArray<(string Density, int Legacy, int Adaptive)> Densities =
        [
            ("mdpi", 48, 108),
            ("hdpi", 72, 162),
            ("xhdpi", 96, 216),
            ("xxhdpi", 144, 324),
            ("xxxhdpi", 192, 432),
        ];

    /// <summary>
    /// The fraction of the adaptive layer a logo may occupy.
    /// </summary>
    /// <remarks>
    /// 66dp of 108dp. Anything outside this is clipped by a circular mask, and
    /// circular masks are the default on most launchers.
    /// </remarks>
    private const double AdaptiveSafeFraction = 66.0 / 108.0;

    /// <summary>Whether a config asks for a generated icon at all.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <returns>The icon's asset reference, or null to keep the shell's placeholder.</returns>
    public static string? IconReference(JsonObject resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return resolved["branding"]?["icon"]?.GetValue<string>();
    }

    /// <summary>Renders every launcher icon file for a config.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <param name="assets">Where the source icon is fetched from.</param>
    /// <param name="images">The resizing pipeline.</param>
    /// <returns>The files to add to the project.</returns>
    /// <exception cref="AssetException">The icon is missing or fails verification.</exception>
    public static ImmutableArray<GeneratedFile> Render(
        JsonObject resolved,
        IAssetStore assets,
        IImagePipeline images)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(images);

        if (IconReference(resolved) is not { } reference)
        {
            return [];
        }

        var source = assets.Read(reference);
        var background = Rgb.Parse(
            resolved["branding"]?["splash"]?["backgroundColor"]?.GetValue<string>() ?? "#FFFFFF");

        var files = new List<GeneratedFile>();

        foreach (var (density, legacy, adaptive) in Densities)
        {
            // The legacy icon is what pre-Android-8 launchers draw directly, so
            // it is flattened: a transparent square icon looks broken there.
            files.Add(new GeneratedFile(
                $"app/src/main/res/mipmap-{density}/ic_launcher.png",
                [.. images.Render(source, new IconSpec("", legacy, Flatten: background))]));

            // The adaptive foreground keeps its transparency — the background
            // layer shows through it — and is inset to the safe zone.
            files.Add(new GeneratedFile(
                $"app/src/main/res/mipmap-{density}/ic_launcher_foreground.png",
                [.. images.Render(source, new IconSpec("", SafeSize(adaptive)))]));
        }

        return [.. files.OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    /// <summary>The pixel size a logo may occupy inside an adaptive layer.</summary>
    /// <param name="layerSize">The full layer size in pixels.</param>
    /// <returns>The safe-zone size.</returns>
    public static int SafeSize(int layerSize) => (int)Math.Round(layerSize * AdaptiveSafeFraction);
}
