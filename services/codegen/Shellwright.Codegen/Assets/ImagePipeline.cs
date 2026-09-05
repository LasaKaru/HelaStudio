using System.Collections.Immutable;
using System.Globalization;
using SkiaSharp;

namespace Shellwright.Codegen.Assets;

/// <summary>A colour, as the config spells it.</summary>
/// <param name="Red">0-255.</param>
/// <param name="Green">0-255.</param>
/// <param name="Blue">0-255.</param>
public readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    /// <summary>Parses <c>#RRGGBB</c>, which is the only form the schema allows.</summary>
    /// <param name="hex">The colour, with or without a leading hash.</param>
    /// <returns>The parsed colour.</returns>
    public static Rgb Parse(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        var text = hex.TrimStart('#');

        return text.Length == 6
            ? new Rgb(
                byte.Parse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(text[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            : throw new FormatException($"'{hex}' is not a #RRGGBB colour.");
    }

    /// <summary>The canonical <c>#RRGGBB</c> spelling, uppercase.</summary>
    /// <returns>The hex form.</returns>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"#{Red:X2}{Green:X2}{Blue:X2}");
}

/// <summary>One image a generated project needs.</summary>
/// <param name="Path">Where it lands, forward-slashed.</param>
/// <param name="Size">Width and height in pixels; icons are always square.</param>
/// <param name="Flatten">A background to composite onto, or null to keep transparency.</param>
public sealed record IconSpec(string Path, int Size, Rgb? Flatten = null);

/// <summary>Turns one source icon into every size a platform needs.</summary>
/// <remarks>
/// An interface because image libraries in .NET carry very different licence
/// terms, and the one this codebase can use may change. Keeping the surface to
/// three methods means swapping is one class. See
/// <see cref="SkiaImagePipeline"/> and docs/adr/0007-image-pipeline.md.
/// </remarks>
public interface IImagePipeline
{
    /// <summary>Reads an image's dimensions and whether it has transparency.</summary>
    /// <param name="source">The encoded source image.</param>
    /// <returns>Its dimensions and alpha state.</returns>
    ImageFacts Inspect(ReadOnlySpan<byte> source);

    /// <summary>Renders one icon at one size.</summary>
    /// <param name="source">The encoded source image.</param>
    /// <param name="spec">The output to produce.</param>
    /// <returns>PNG bytes.</returns>
    byte[] Render(ReadOnlySpan<byte> source, IconSpec spec);

    /// <summary>Renders a solid-colour PNG, for a flattened background layer.</summary>
    /// <param name="colour">The fill.</param>
    /// <param name="size">Width and height in pixels.</param>
    /// <returns>PNG bytes.</returns>
    byte[] RenderSolid(Rgb colour, int size);
}

/// <summary>What an image is, before anything is done to it.</summary>
/// <param name="Width">Pixels.</param>
/// <param name="Height">Pixels.</param>
/// <param name="HasAlpha">Whether any pixel is not fully opaque.</param>
public sealed record ImageFacts(int Width, int Height, bool HasAlpha);

/// <summary>
/// The SkiaSharp implementation.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Determinism is the whole difficulty here.</b> An image pipeline that
/// resizes "correctly" but not identically breaks the build cache in the least
/// visible way possible: the icons look right, and every rebuild recompiles.
/// Three things are pinned rather than left to defaults:
/// </para>
/// <list type="number">
///   <item>
///     The resampler is stated explicitly. Library defaults change between
///     versions, and a changed default is a silent, repo-wide cache miss.
///   </item>
///   <item>
///     PNG output is asserted to carry no ancillary chunks. An encoder that
///     stamps a creation time produces a different file every single run.
///   </item>
///   <item>
///     The library version is pinned centrally and belongs to the toolchain
///     descriptor, so a bump invalidates the cache deliberately rather than
///     mysteriously.
///   </item>
/// </list>
/// </remarks>
public sealed class SkiaImagePipeline : IImagePipeline
{
    /// <summary>
    /// The resampler, stated rather than defaulted.
    /// </summary>
    /// <remarks>
    /// Mitchell cubic because icons are downscaled by large factors — a 1024px
    /// source to 48px — and nearest or bilinear sampling turns fine detail into
    /// aliasing at that ratio. Mitchell is the standard high-quality choice for
    /// downscaling and trades a little sharpness for an absence of ringing,
    /// which matters on a logo.
    /// </remarks>
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    /// <inheritdoc/>
    public ImageFacts Inspect(ReadOnlySpan<byte> source)
    {
        using var bitmap = Decode(source);

        var hasAlpha = false;

        for (var y = 0; y < bitmap.Height && !hasAlpha; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha != byte.MaxValue)
                {
                    hasAlpha = true;
                    break;
                }
            }
        }

        return new ImageFacts(bitmap.Width, bitmap.Height, hasAlpha);
    }

    /// <inheritdoc/>
    public byte[] Render(ReadOnlySpan<byte> source, IconSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        using var bitmap = Decode(source);

        var info = new SKImageInfo(spec.Size, spec.Size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var surface = new SKBitmap(info);

        using (var canvas = new SKCanvas(surface))
        {
            // Flattening is a fill behind the artwork, not a post-process: an
            // icon composited onto its background has no partially transparent
            // edge pixels left, which is what Apple's "no alpha channel" rule
            // actually requires.
            canvas.Clear(spec.Flatten is { } fill
                ? new SKColor(fill.Red, fill.Green, fill.Blue)
                : SKColors.Transparent);

            // Aspect is preserved and the result centred. Icons are validated
            // square on upload, so this only matters for a source that slipped
            // through — and padding is the safe answer, because cropping would
            // silently cut part off a customer's logo.
            var scale = Math.Min((float)spec.Size / bitmap.Width, (float)spec.Size / bitmap.Height);
            var width = bitmap.Width * scale;
            var height = bitmap.Height * scale;

            using var image = SKImage.FromBitmap(bitmap);

            canvas.DrawImage(
                image,
                SKRect.Create((spec.Size - width) / 2, (spec.Size - height) / 2, width, height),
                Sampling);
        }

        return Encode(surface);
    }

    /// <inheritdoc/>
    public byte[] RenderSolid(Rgb colour, int size)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(colour.Red, colour.Green, colour.Blue));
        }

        return Encode(bitmap);
    }

    private static SKBitmap Decode(ReadOnlySpan<byte> source)
    {
        using var data = SKData.CreateCopy(source.ToArray());

        // Unpremultiplied, so alpha inspection reads the values the source
        // actually carries rather than values scaled by their own alpha.
        return SKBitmap.Decode(data, new SKImageInfo(0, 0, SKColorType.Rgba8888, SKAlphaType.Unpremul))
            ?? SKBitmap.Decode(data)
            ?? throw new ArgumentException("Not a decodable image.", nameof(source));
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");

        return encoded.ToArray();
    }
}
