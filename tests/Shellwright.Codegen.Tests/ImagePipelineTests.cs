using FluentAssertions;
using Shellwright.Codegen.Assets;
using SkiaSharp;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>
/// The asset pipeline's two obligations: correct output, and the same output
/// every time.
/// </summary>
/// <remarks>
/// ⚠️ The second is easy to lose without noticing. An icon that resizes
/// "correctly" but not identically breaks the build cache in the least visible
/// way there is — the icons look right and every rebuild recompiles.
/// </remarks>
public sealed class ImagePipelineTests
{
    private static readonly SkiaImagePipeline Pipeline = new();

    /// <summary>A square source with a transparent corner and a coloured body.</summary>
    private static byte[] SourceIcon(int size = 1024, bool withAlpha = true)
    {
        using var bitmap = new SKBitmap(
            new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var transparent = withAlpha && x < size / 4 && y < size / 4;
                bitmap.SetPixel(x, y, transparent
                    ? new SKColor(0, 0, 0, 0)
                    : new SKColor((byte)(x % 256), (byte)(y % 256), 128, 255));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>TC-S04-GEN-021 — every requested size is produced.</summary>
    [Theory]
    [InlineData(48)]
    [InlineData(72)]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    [InlineData(1024)]
    public void RendersTheRequestedSize(int size)
    {
        var png = Pipeline.Render(SourceIcon(), new IconSpec("icon.png", size));

        using var rendered = SKBitmap.Decode(png);

        rendered.Width.Should().Be(size);
        rendered.Height.Should().Be(size);
    }

    /// <summary>
    /// The same source and spec produce the same bytes, every time.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the property the whole build cache rests on for assets. It
    /// holds only because the resampler and the PNG encoder are both pinned
    /// explicitly rather than left to library defaults, and because every
    /// ancillary chunk — a creation timestamp above all — is excluded.
    /// </remarks>
    [Fact]
    public void RenderingTwiceIsByteIdentical()
    {
        var source = SourceIcon();
        var spec = new IconSpec("icon.png", 192);

        Pipeline.Render(source, spec).Should().Equal(Pipeline.Render(source, spec));
    }

    /// <summary>No PNG carries a timestamp or any other ancillary chunk.</summary>
    /// <remarks>
    /// The direct test of the thing that would silently break determinism. A
    /// tIME chunk alone makes every icon differ from the last.
    /// </remarks>
    [Fact]
    public void RenderedPngHasNoAncillaryChunks()
    {
        var png = Pipeline.Render(SourceIcon(), new IconSpec("icon.png", 96));
        var text = System.Text.Encoding.ASCII.GetString(png);

        foreach (var chunk in new[] { "tIME", "tEXt", "iTXt", "zTXt", "gAMA", "iCCP", "pHYs" })
        {
            text.Should().NotContain(chunk, "{0} would vary between runs or machines", chunk);
        }
    }

    /// <summary>TC-S04-GEN-024 — flattening removes transparency completely.</summary>
    /// <remarks>
    /// ⚠️ Apple rejects an app icon with an alpha channel outright. "Mostly
    /// opaque" is not good enough, so this asserts every pixel.
    /// </remarks>
    [Fact]
    public void FlatteningLeavesNoTransparentPixel()
    {
        var flattened = Pipeline.Render(
            SourceIcon(),
            new IconSpec("icon.png", 128, Flatten: new Rgb(0xFF, 0xFF, 0xFF)));

        Pipeline.Inspect(flattened).HasAlpha.Should().BeFalse();
    }

    /// <summary>Without flattening, transparency survives.</summary>
    /// <remarks>
    /// Android's adaptive-icon foreground layer needs it, so flattening must be
    /// a choice rather than something the pipeline always does.
    /// </remarks>
    [Fact]
    public void TransparencyIsKeptWhenNotFlattening()
    {
        var kept = Pipeline.Render(SourceIcon(), new IconSpec("icon.png", 128));

        Pipeline.Inspect(kept).HasAlpha.Should().BeTrue();
    }

    /// <summary>Inspection reports what the validator needs to warn about.</summary>
    [Fact]
    public void InspectReportsDimensionsAndAlpha()
    {
        Pipeline.Inspect(SourceIcon(512)).Should().Be(new ImageFacts(512, 512, true));
        Pipeline.Inspect(SourceIcon(512, withAlpha: false)).Should().Be(new ImageFacts(512, 512, false));
    }

    /// <summary>A solid colour renders at the requested size, fully opaque.</summary>
    [Fact]
    public void SolidColourIsOpaque()
    {
        var png = Pipeline.RenderSolid(new Rgb(0x0B, 0x12, 0x20), 108);

        using var image = SKBitmap.Decode(png);

        image.Width.Should().Be(108);
        image.GetPixel(0, 0).Should().Be(new SKColor(0x0B, 0x12, 0x20, 255));
    }

    /// <summary>Colours round-trip through the config's hex spelling.</summary>
    [Theory]
    [InlineData("#0B1220", 0x0B, 0x12, 0x20)]
    [InlineData("2563EB", 0x25, 0x63, 0xEB)]
    [InlineData("#ffffff", 0xFF, 0xFF, 0xFF)]
    public void ParsesConfigColours(string hex, byte r, byte g, byte b)
    {
        var colour = Rgb.Parse(hex);

        colour.Should().Be(new Rgb(r, g, b));
        Rgb.Parse(colour.ToString()).Should().Be(colour);
    }

    /// <summary>A malformed colour is refused rather than guessed at.</summary>
    [Theory]
    [InlineData("#FFF")]
    [InlineData("not-a-colour")]
    [InlineData("")]
    public void RefusesMalformedColours(string hex)
    {
        var act = () => Rgb.Parse(hex);

        act.Should().Throw<Exception>();
    }
}
