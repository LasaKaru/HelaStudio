using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Shellwright.Codegen;
using Shellwright.Codegen.Ios;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>What the iOS generator actually emits.</summary>
/// <remarks>
/// ⚠️ These assert the files, not a build. Nothing here can run
/// <c>xcodebuild</c>, <c>xcodegen</c> or <c>plutil</c> — that happens on a Mac,
/// in the Codemagic <c>ios-verify</c> workflow. Sprint 04 established that a
/// snapshot proves what the generator produced, not that the toolchain accepts
/// it, and the same limit applies here twice over.
/// </remarks>
public sealed class IosTests
{
    private static async Task<InMemoryFileSink> GenerateAsync(string fixture)
    {
        var sink = new InMemoryFileSink();
        await Fixtures.IosGenerator()
            .GenerateAsync(Fixtures.Resolve(fixture), ToolchainDescriptor.Ios, sink);
        return sink;
    }

    /// <summary>TC-S05-GEN-016 — every emitted plist parses.</summary>
    /// <remarks>
    /// The cheapest available stand-in for <c>plutil -lint</c>, which needs a
    /// Mac. A malformed plist is not a build warning on iOS; the file is simply
    /// ignored or the build fails with a message that names neither the key nor
    /// the cause.
    /// </remarks>
    [Theory]
    [InlineData("minimal.json")]
    [InlineData("maximal.json")]
    [InlineData("unicode.json")]
    [InlineData("edge-hostile-text.json")]
    public async Task EveryPlistParses(string fixture)
    {
        var sink = await GenerateAsync(fixture);

        foreach (var file in sink.Files.Where(IsPlist))
        {
            var act = () => XDocument.Parse(sink.Text(file.Path));
            act.Should().NotThrow("{0} must be a well-formed plist", file.Path);
        }
    }

    private static bool IsPlist(GeneratedFile file) =>
        file.Path.EndsWith(".plist", StringComparison.Ordinal)
        || file.Path.EndsWith(".entitlements", StringComparison.Ordinal)
        || file.Path.EndsWith(".xcprivacy", StringComparison.Ordinal);

    /// <summary>
    /// TC-S05-GEN-015 — the privacy manifest is emitted even with no plugins.
    /// </summary>
    /// <remarks>
    /// ⚠️ Required, and easy to believe otherwise. Apple checks the manifest at
    /// upload and rejects a binary whose required-reason API usage is
    /// undeclared. The shell reads <c>UserDefaults</c> to remember the last
    /// loaded URL, and that alone makes the file mandatory — omitting it does
    /// not mean "we collect nothing", it means the upload fails.
    /// </remarks>
    [Fact]
    public async Task PrivacyManifestIsPresentWithTheUserDefaultsReason()
    {
        var sink = await GenerateAsync("minimal.json");
        var manifest = sink.Text("Resources/PrivacyInfo.xcprivacy");

        manifest.Should().Contain("NSPrivacyAccessedAPICategoryUserDefaults");
        manifest.Should().Contain("CA92.1");
    }

    /// <summary>TC-S05-GEN-011 — the encryption declaration is always present.</summary>
    /// <remarks>
    /// Omitting it does not default it: App Store Connect stops and asks the
    /// customer a compliance question on every single upload, forever. Setting
    /// it saves them a step per release for the cost of one line.
    /// </remarks>
    [Fact]
    public async Task EncryptionDeclarationIsAlwaysEmitted()
    {
        var sink = await GenerateAsync("minimal.json");

        sink.Text("project.yml").Should().Contain("ITSAppUsesNonExemptEncryption: false");
    }

    /// <summary>TC-S05-GEN-009 — a permission brings its usage string.</summary>
    /// <remarks>
    /// ⚠️ The consequence of getting this wrong is not a warning. An app that
    /// reaches the camera with no usage string is killed by the system the
    /// instant a web form asks, on a real device, with nothing to connect the
    /// crash to a missing plist key.
    /// </remarks>
    [Fact]
    public void GrantedPermissionsGetUsageStrings()
    {
        var resolved = Fixtures.Resolve("minimal.json");
        resolved["permissions"] = new JsonObject { ["camera"] = true, ["microphone"] = true };

        var strings = IosProjectGenerator.UsageDescriptions(resolved);

        strings.Keys.Should().Equal("NSCameraUsageDescription", "NSMicrophoneUsageDescription");
        strings.Values.Should().OnlyContain(value => value.Length > 0);
    }

    /// <summary>An always-location request also carries the when-in-use string.</summary>
    /// <remarks>
    /// ⚠️ iOS requires both. Requesting Always with only the Always string
    /// produces a dialog the user cannot grant, which reads as the feature
    /// simply not working.
    /// </remarks>
    [Fact]
    public void AlwaysLocationCarriesBothStrings()
    {
        var resolved = Fixtures.Resolve("minimal.json");
        resolved["permissions"] = new JsonObject { ["location"] = "always" };

        IosProjectGenerator.UsageDescriptions(resolved).Keys.Should().Equal(
            "NSLocationAlwaysAndWhenInUseUsageDescription",
            "NSLocationWhenInUseUsageDescription");
    }

    /// <summary>A config granting nothing gets no usage strings at all.</summary>
    /// <remarks>
    /// The mirror-image failure: Apple's static analysis flags a usage string
    /// for a capability the binary cannot reach, so a permanently-present
    /// string is a rejection risk rather than harmless.
    /// </remarks>
    [Fact]
    public void UngrantedPermissionsGetNoUsageStrings() =>
        IosProjectGenerator.UsageDescriptions(Fixtures.Resolve("minimal.json")).Should().BeEmpty();

    /// <summary>TC-S05-GEN-013 — associated domains, one per host, sorted.</summary>
    [Fact]
    public void AssociatedDomainsAreSorted()
    {
        var resolved = Fixtures.Resolve("edge-hostile-text.json");

        IosProjectGenerator.AssociatedDomains(resolved).Should().Equal(
            "applinks:a.bobs.example",
            "applinks:m.bobs.example",
            "applinks:order.bobs.example");
    }

    /// <summary>A config with no Universal Links emits no entitlement.</summary>
    /// <remarks>
    /// An empty associated-domains array is not the same as omitting the key:
    /// it still requires the capability on the provisioning profile, which
    /// fails signing for a customer who never asked for deep links.
    /// </remarks>
    [Fact]
    public async Task NoUniversalLinksMeansNoAssociatedDomains()
    {
        var sink = await GenerateAsync("minimal.json");

        sink.Text("Resources/Shellwright.entitlements")
            .Should().NotContain("associated-domains");
    }

    /// <summary>iPad orientations are declared separately from iPhone's.</summary>
    /// <remarks>
    /// ⚠️ An app that restricts orientation on iPad is rejected without
    /// justification, and the same app may legitimately be portrait-only on
    /// iPhone — so one list for both is wrong in one direction or the other.
    /// </remarks>
    [Fact]
    public async Task IpadOrientationsAreDeclaredSeparately()
    {
        var sink = await GenerateAsync("edge-portrait-locked.json");
        var spec = sink.Text("project.yml");

        spec.Should().Contain("UISupportedInterfaceOrientations~ipad");
        spec.Should().Contain("UIInterfaceOrientationPortraitUpsideDown");
    }

    /// <summary>Orientation follows the config.</summary>
    [Theory]
    [InlineData("edge-portrait-locked.json", 1)]
    [InlineData("minimal.json", 3)]
    public void OrientationsFollowTheConfig(string fixture, int expected) =>
        IosProjectGenerator.Orientations(Fixtures.Resolve(fixture)).Should().HaveCount(expected);

    /// <summary>
    /// The signing identity is a placeholder, never a value.
    /// </summary>
    /// <remarks>
    /// ⚠️ It is customer-specific and, from Sprint 14, secret. A generated
    /// project is cached, exported, and handed to the customer — baking a team
    /// id into it would leak one customer's identity into another's build.
    /// </remarks>
    [Fact]
    public async Task SigningIsAPlaceholder()
    {
        var sink = await GenerateAsync("maximal.json");
        var spec = sink.Text("project.yml");

        spec.Should().Contain("CODE_SIGN_STYLE: Manual");
        spec.Should().Contain("$(SHELLWRIGHT_TEAM_ID)");
        spec.Should().NotContain("DEVELOPMENT_TEAM: ABCDE");
    }

    /// <summary>An app name with YAML metacharacters survives the round trip.</summary>
    /// <remarks>
    /// ⚠️ The reason every interpolation in the template is quoted. Unquoted,
    /// a leading <c>@</c> is reserved YAML syntax, a colon-space starts a
    /// mapping, and the spec fails to parse — for the customer whose app is
    /// called "Bob's Diner: Orders".
    /// </remarks>
    [Fact]
    public async Task AHostileAppNameSurvivesTheSpec()
    {
        var sink = await GenerateAsync("edge-hostile-text.json");

        sink.Text("project.yml").Should().Contain(@"CFBundleDisplayName: ""@Bob's \""Diner\"" & Grill <$5""");
    }

    /// <summary>The generated project carries a build script that stands alone.</summary>
    /// <remarks>
    /// The beginning of source export (BD-10): a customer who leaves should be
    /// able to build what they were paying for.
    /// </remarks>
    [Fact]
    public async Task BuildScriptIsEmitted()
    {
        var sink = await GenerateAsync("maximal.json");
        var script = sink.Text("build.sh");

        script.Should().StartWith("#!/bin/sh");
        script.Should().Contain("xcodegen generate");
        script.Should().Contain("acme_orders.xcodeproj");
    }

    /// <summary>The app icon is a single 1024px image, flattened.</summary>
    /// <remarks>
    /// ⚠️ Apple rejects an app icon with an alpha channel at upload, naming
    /// neither the file nor the channel. Xcode 14 and later generate every
    /// other size from this one, which removes fourteen chances to get a
    /// dimension wrong.
    /// </remarks>
    [Fact]
    public async Task AppIconIsOneFlattenedImage()
    {
        var sink = await GenerateAsync("maximal.json");
        var icon = sink.Find("Resources/Assets.xcassets/AppIcon.appiconset/icon-1024.png");

        icon.Should().NotBeNull();

        using var image = SkiaSharp.SKBitmap.Decode(icon!.Content.AsSpan().ToArray());
        image.Width.Should().Be(1024);

        new Assets.SkiaImagePipeline().Inspect(icon.Content.AsSpan()).HasAlpha.Should().BeFalse();
    }

    /// <summary>Colour sets always carry both appearances.</summary>
    /// <remarks>
    /// A colour set with only a light appearance stays light in dark mode
    /// rather than falling back gracefully — exactly the "looks like a
    /// wrapper" tell the product exists to avoid.
    /// </remarks>
    [Fact]
    public async Task ColourSetsCarryLightAndDark()
    {
        var sink = await GenerateAsync("maximal.json");
        var colours = sink.Text("Resources/Assets.xcassets/SplashBackground.colorset/Contents.json");

        JsonNode.Parse(colours)!["colors"]!.AsArray().Should().HaveCount(2);
        colours.Should().Contain("luminosity");
    }

    /// <summary>Generating twice is byte-identical, as on Android.</summary>
    [Theory]
    [InlineData("minimal.json")]
    [InlineData("maximal.json")]
    [InlineData("edge-hostile-text.json")]
    public async Task GeneratingTwiceIsByteIdentical(string fixture)
    {
        var first = await GenerateAsync(fixture);
        var second = await GenerateAsync(fixture);

        second.Files.Select(file => file.Path).Should().Equal(first.Files.Select(file => file.Path));

        foreach (var expected in first.Files)
        {
            second.Find(expected.Path)!.Content
                .Should().Equal(expected.Content, "{0} differed between two runs", expected.Path);
        }
    }

    /// <summary>The manifest records the iOS toolchain, including XcodeGen.</summary>
    /// <remarks>
    /// ⚠️ XcodeGen decides the bytes of the project it produces, and Xcode's
    /// project format shifts roughly annually. Both belong in the cache key, or
    /// a version bump surfaces as a mysterious full rebuild rather than a
    /// deliberate invalidation.
    /// </remarks>
    [Fact]
    public async Task ManifestRecordsTheIosToolchain()
    {
        var sink = await GenerateAsync("minimal.json");
        var manifest = JsonNode.Parse(sink.Text(".shellwright/manifest.json"))!;

        manifest["platform"]!.GetValue<string>().Should().Be("ios");
        manifest["toolchain"]!["xcodegen"].Should().NotBeNull();
        manifest["toolchain"]!["xcode"].Should().NotBeNull();
    }
}
