using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shellwright.Codegen;
using Shellwright.Codegen.Android;
using Shellwright.Codegen.Assets;
using SkiaSharp;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>Resolving the content-addressed assets a config points at.</summary>
public sealed class AssetStoreTests
{
    /// <summary>A reference names the content, so content that does not match is refused.</summary>
    /// <remarks>
    /// ⚠️ Content addressing that nobody verifies is decoration. These bytes go
    /// straight into a signed binary that ships to a customer's users, so a
    /// store that returned whatever it found under the name would turn a
    /// corrupted file — or a substituted one — into a shipped app icon.
    /// </remarks>
    [Fact]
    public void ContentNotMatchingItsAddressIsRefused()
    {
        var root = Directory.CreateTempSubdirectory("shellwright-assets-");

        try
        {
            var honest = Encoding.UTF8.GetBytes("the real content");
            var digest = Convert.ToHexStringLower(SHA256.HashData(honest));

            File.WriteAllBytes(Path.Combine(root.FullName, $"sha256-{digest}.png"), "tampered"u8.ToArray());

            var act = () => new DirectoryAssetStore(root.FullName).Read($"asset://sha256-{digest}");

            act.Should().Throw<AssetException>().WithMessage("*not to its own address*");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>A missing asset names itself in the error.</summary>
    [Fact]
    public void MissingAssetIsNamed()
    {
        var reference = "asset://sha256-" + new string('a', 64);

        var act = () => new DirectoryAssetStore("/nonexistent-asset-root").Read(reference);

        act.Should().Throw<AssetException>().WithMessage($"*{reference}*");
    }

    /// <summary>A malformed reference is rejected before anything is read.</summary>
    [Theory]
    [InlineData("https://example.com/icon.png")]
    [InlineData("asset://sha256-tooshort")]
    [InlineData("asset://sha256-" + "A0B1C2D3E4F5A6B7C8D9E0F1A2B3C4D5E6F7A8B9C0D1E2F3A4B5C6D7E8F9A0B1")]
    public void MalformedReferencesAreRejected(string reference)
    {
        var act = () => new DirectoryAssetStore("/tmp").Read(reference);

        act.Should().Throw<AssetException>();
    }

    /// <summary>An in-memory store round-trips what it was given.</summary>
    [Fact]
    public void InMemoryStoreRoundTrips()
    {
        var store = new InMemoryAssetStore();
        var content = Encoding.UTF8.GetBytes("some bytes");

        var reference = store.Add(content);

        store.Read(reference).Should().Equal(content);
    }

    /// <summary>The committed fixture icon verifies against its own filename.</summary>
    /// <remarks>
    /// The corpus is only meaningful if the fixture asset is genuinely
    /// content-addressed rather than named by hand.
    /// </remarks>
    [Fact]
    public void FixtureIconVerifies()
    {
        var reference = Fixtures.Resolve("maximal.json")["branding"]!["icon"]!.GetValue<string>();

        var act = () => Fixtures.Assets().Read(reference);

        act.Should().NotThrow();
    }
}

/// <summary>Generating the Android launcher icon set.</summary>
public sealed class AndroidIconTests
{
    private static async Task<InMemoryFileSink> GenerateAsync(string fixture)
    {
        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.Resolve(fixture), ToolchainDescriptor.Android, sink);
        return sink;
    }

    /// <summary>TC-S04-GEN-021 — every density gets both layers.</summary>
    [Fact]
    public async Task EveryDensityGetsLegacyAndAdaptiveLayers()
    {
        var sink = await GenerateAsync("maximal.json");

        foreach (var density in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
        {
            sink.Find($"app/src/main/res/mipmap-{density}/ic_launcher.png")
                .Should().NotBeNull("{0} needs a legacy icon", density);
            sink.Find($"app/src/main/res/mipmap-{density}/ic_launcher_foreground.png")
                .Should().NotBeNull("{0} needs an adaptive foreground", density);
        }
    }

    /// <summary>Legacy icons are rendered at the density's pixel size.</summary>
    [Theory]
    [InlineData("mdpi", 48)]
    [InlineData("hdpi", 72)]
    [InlineData("xhdpi", 96)]
    [InlineData("xxhdpi", 144)]
    [InlineData("xxxhdpi", 192)]
    public async Task LegacyIconsAreTheRightSize(string density, int expected)
    {
        var sink = await GenerateAsync("maximal.json");
        var png = sink.Find($"app/src/main/res/mipmap-{density}/ic_launcher.png")!;

        using var image = SKBitmap.Decode(png.Content.AsSpan().ToArray());

        image.Width.Should().Be(expected);
    }

    /// <summary>
    /// TC-S04-GEN-021 — the adaptive foreground stays inside the safe zone.
    /// </summary>
    /// <remarks>
    /// ⚠️ The adaptive layer is 108dp but launchers mask it, so only the central
    /// 66dp is safe on a circular mask — the default on most launchers. Scaling
    /// artwork to the full layer puts a third of it outside the mask, and the
    /// customer sees a clipped icon on exactly the devices their users have.
    /// </remarks>
    [Theory]
    [InlineData(108, 66)]
    [InlineData(162, 99)]
    [InlineData(432, 264)]
    public void AdaptiveForegroundUsesTheSafeZone(int layer, int expected) =>
        AndroidIcons.SafeSize(layer).Should().Be(expected);

    /// <summary>The legacy icon is flattened; the adaptive foreground is not.</summary>
    /// <remarks>
    /// <para>
    /// A transparent square looks broken on the pre-Android-8 launchers that
    /// draw the legacy bitmap directly, while the adaptive foreground must keep
    /// its transparency so the background layer shows through.
    /// </para>
    /// <para>
    /// ⚠️ Uses a source that actually has transparency. Asserting this against
    /// the committed fixture icon would prove nothing: that icon is fully
    /// opaque, as the schema asks for, so its foreground layer is opaque
    /// whatever the pipeline does. A test that passes because of its input
    /// rather than its subject is worse than no test.
    /// </para>
    /// </remarks>
    [Fact]
    public void LegacyIsFlattenedAndAdaptiveIsNot()
    {
        var pipeline = new SkiaImagePipeline();
        var source = TransparentSource();

        pipeline.Inspect(pipeline.Render(source, new IconSpec("", 48, Flatten: new Rgb(0, 0, 0))))
            .HasAlpha.Should().BeFalse("the legacy bitmap is drawn directly and must be opaque");

        pipeline.Inspect(pipeline.Render(source, new IconSpec("", 66)))
            .HasAlpha.Should().BeTrue("the adaptive foreground sits over a background layer");
    }

    /// <summary>A source with a genuinely transparent region.</summary>
    private static byte[] TransparentSource()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                bitmap.SetPixel(x, y, x < 64 ? new SKColor(0, 0, 0, 0) : new SKColor(0x25, 0x63, 0xEB, 255));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A config with no icon keeps the shell's placeholder.</summary>
    /// <remarks>
    /// The generated project must still build and still look like something,
    /// so an absent icon is not an error.
    /// </remarks>
    [Fact]
    public async Task NoIconKeepsThePlaceholder()
    {
        var sink = await GenerateAsync("minimal.json");

        sink.Find("app/src/main/res/mipmap-mdpi/ic_launcher.png").Should().BeNull();
        sink.Find("app/src/main/res/drawable/ic_launcher_foreground.xml").Should().NotBeNull();
        sink.Text("app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml")
            .Should().Contain("@drawable/ic_launcher_foreground");
    }

    /// <summary>
    /// A generated icon replaces the placeholder rather than shipping beside it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Two things go wrong if the adaptive XML is not switched over: the
    /// customer's app shows the placeholder mark on every Android 8 and later
    /// device, and lint fails their build over the now-unused drawable. Both
    /// were real, and both were found by building a generated project rather
    /// than by reading the diff.
    /// </remarks>
    [Fact]
    public async Task GeneratedIconReplacesThePlaceholder()
    {
        var sink = await GenerateAsync("maximal.json");

        sink.Text("app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml")
            .Should().Contain("@mipmap/ic_launcher_foreground");
        sink.Find("app/src/main/res/drawable/ic_launcher_foreground.xml").Should().BeNull();
    }

    /// <summary>A config naming an icon that cannot be resolved fails loudly.</summary>
    /// <remarks>
    /// Shipping a customer's app under a placeholder icon is worse than not
    /// shipping it, so this is deliberately not a fall back.
    /// </remarks>
    [Fact]
    public async Task AnUnresolvableIconFailsGeneration()
    {
        var resolved = Fixtures.Resolve("minimal.json");

        // Set the one key, rather than replacing the whole branding object:
        // the templates read resolved defaults such as splash.backgroundColor,
        // and a wholesale replacement would fail rendering instead of asset
        // resolution, which is not what this test is about.
        resolved["branding"]!["icon"] = "asset://sha256-" + new string('b', 64);

        var act = async () => await Fixtures.Generator()
            .GenerateAsync(resolved, ToolchainDescriptor.Android, new InMemoryFileSink());

        await act.Should().ThrowAsync<AssetException>();
    }

    /// <summary>A customer's project does not ship the shell's own test suite.</summary>
    /// <remarks>
    /// Those tests read <c>tests/fixtures/</c>, which does not exist in a
    /// generated project. A customer opening their exported source to find
    /// someone else's failing tests is being handed confusion, not value.
    /// </remarks>
    [Fact]
    public async Task GeneratedProjectsDoNotShipTheShellsTests()
    {
        var sink = await GenerateAsync("minimal.json");

        sink.Files.Should().NotContain(file =>
            file.Path.StartsWith("app/src/test/", StringComparison.Ordinal));
    }

    /// <summary>Location is granted as both fine and coarse, never fine alone.</summary>
    /// <remarks>
    /// ⚠️ Since Android 12 a FINE-only request fails at the permission dialog:
    /// the user is offered an "approximate location" choice the app never
    /// declared. The consequence is not a lint error, it is that location never
    /// works for that customer.
    /// </remarks>
    [Fact]
    public async Task LocationGrantsBothPrecisions()
    {
        var sink = await GenerateAsync("maximal.json");
        var manifest = sink.Text("app/src/main/AndroidManifest.xml");

        manifest.Should().Contain("android.permission.ACCESS_COARSE_LOCATION");
        manifest.Should().Contain("android.permission.ACCESS_FINE_LOCATION");
    }
}
