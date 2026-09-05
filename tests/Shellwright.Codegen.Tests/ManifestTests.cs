using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Shellwright.Codegen;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>What the generated Android manifest and Gradle script actually say.</summary>
public sealed class ManifestTests
{
    private static async Task<InMemoryFileSink> GenerateAsync(string fixture)
    {
        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.Resolve(fixture), ToolchainDescriptor.Android, sink);
        return sink;
    }

    /// <summary>TC-S04-GEN-013 — App Links filters, one per host, sorted.</summary>
    /// <remarks>
    /// ⚠️ Sorted because the config's array order is the customer's, and an
    /// order change with no behaviour change must not invalidate the cache.
    /// </remarks>
    [Fact]
    public async Task DeepLinkFiltersAreOnePerHostAndSorted()
    {
        var sink = await GenerateAsync("edge-hostile-text.json");
        var android = XNamespace.Get("http://schemas.android.com/apk/res/android");

        var manifest = XDocument.Parse(sink.Text("app/src/main/AndroidManifest.xml"));

        var verified = manifest.Descendants("intent-filter")
            .Where(filter => (string?)filter.Attribute(android + "autoVerify") == "true")
            .SelectMany(filter => filter.Elements("data"))
            .Select(data => (string?)data.Attribute(android + "host"))
            .ToList();

        // The fixture lists them as order, a, m — the generator must not.
        verified.Should().Equal("a.bobs.example", "m.bobs.example", "order.bobs.example");
    }

    /// <summary>A custom scheme becomes its own filter, without autoVerify.</summary>
    /// <remarks>
    /// autoVerify is for App Links over https, which are backed by a file
    /// served from the domain. A private scheme has nothing to verify against,
    /// and marking it verified makes Android reject the whole manifest entry.
    /// </remarks>
    [Fact]
    public async Task CustomSchemeGetsAnUnverifiedFilter()
    {
        var sink = await GenerateAsync("edge-hostile-text.json");
        var android = XNamespace.Get("http://schemas.android.com/apk/res/android");

        var manifest = XDocument.Parse(sink.Text("app/src/main/AndroidManifest.xml"));

        var custom = manifest.Descendants("data")
            .Single(data => (string?)data.Attribute(android + "scheme") == "bobs");

        custom.Parent!.Attribute(android + "autoVerify").Should().BeNull();
    }

    /// <summary>A config with no deep links emits no App Links filters at all.</summary>
    [Fact]
    public async Task NoDeepLinksMeansNoFilters()
    {
        var sink = await GenerateAsync("minimal.json");
        var android = XNamespace.Get("http://schemas.android.com/apk/res/android");

        XDocument.Parse(sink.Text("app/src/main/AndroidManifest.xml"))
            .Descendants("intent-filter")
            .Where(filter => filter.Attribute(android + "autoVerify") is not null)
            .Should().BeEmpty();
    }

    /// <summary>Unjustified permissions are removed, not merely omitted.</summary>
    /// <remarks>
    /// ⚠️ Omitting is not enough: a dependency can declare a permission on the
    /// app's behalf during manifest merging, and the customer then ships an app
    /// asking for a camera it never uses — one of the most common store
    /// rejections, and invisible until review.
    /// </remarks>
    [Fact]
    public async Task UnjustifiedPermissionsAreRemovedNotOmitted()
    {
        var sink = await GenerateAsync("minimal.json");
        var android = XNamespace.Get("http://schemas.android.com/apk/res/android");
        var tools = XNamespace.Get("http://schemas.android.com/tools");

        var removed = XDocument.Parse(sink.Text("app/src/main/AndroidManifest.xml"))
            .Descendants("uses-permission")
            .Where(node => (string?)node.Attribute(tools + "node") == "remove")
            .Select(node => (string?)node.Attribute(android + "name"))
            .ToList();

        removed.Should().Contain("android.permission.CAMERA");
        removed.Should().Contain("android.permission.RECORD_AUDIO");
        removed.Should().Contain("android.permission.ACCESS_FINE_LOCATION");
        removed.Should().Contain("android.permission.POST_NOTIFICATIONS");
    }

    /// <summary>A justified permission is granted rather than removed.</summary>
    [Fact]
    public async Task JustifiedPermissionsAreGranted()
    {
        var resolved = Fixtures.Resolve("minimal.json");
        resolved["permissions"] = new JsonObject { ["camera"] = true };

        var sink = new InMemoryFileSink();
        await Fixtures.Generator().GenerateAsync(resolved, ToolchainDescriptor.Android, sink);

        var android = XNamespace.Get("http://schemas.android.com/apk/res/android");
        var tools = XNamespace.Get("http://schemas.android.com/tools");

        var camera = XDocument.Parse(sink.Text("app/src/main/AndroidManifest.xml"))
            .Descendants("uses-permission")
            .Single(node => (string?)node.Attribute(android + "name") == "android.permission.CAMERA");

        camera.Attribute(tools + "node").Should().BeNull("a granted permission must not also be removed");
    }

    /// <summary>"any" emits no orientation attribute at all.</summary>
    /// <remarks>
    /// ⚠️ <c>unspecified</c> and omitting the attribute behave identically at
    /// runtime, but Android lint flags the attribute's mere presence
    /// (<c>DiscouragedApi</c>) — and lint runs with warnings as errors in every
    /// generated project, so emitting a redundant attribute fails a customer's
    /// build for nothing.
    /// </remarks>
    [Fact]
    public async Task AnyOrientationEmitsNoAttribute()
    {
        var sink = await GenerateAsync("minimal.json");

        sink.Text("app/src/main/AndroidManifest.xml").Should().NotContain("screenOrientation");
    }

    /// <summary>A fixed orientation is emitted with both lint checks suppressed.</summary>
    /// <remarks>
    /// ⚠️ Two checks fire, not one: <c>DiscouragedApi</c> objects to the
    /// attribute existing, and <c>LockedOrientationActivity</c> objects to the
    /// value not being <c>unspecified</c> or <c>fullSensor</c>. Suppressing only
    /// the first still fails the build — which is how this was found, by
    /// building a generated portrait project rather than by reading the diff.
    /// </remarks>
    [Fact]
    public async Task FixedOrientationSuppressesBothLintChecks()
    {
        var sink = await GenerateAsync("edge-portrait-locked.json");
        var android = XNamespace.Get("http://schemas.android.com/apk/res/android");
        var tools = XNamespace.Get("http://schemas.android.com/tools");

        var activity = XDocument.Parse(sink.Text("app/src/main/AndroidManifest.xml"))
            .Descendants("activity")
            .Single();

        activity.Attribute(android + "screenOrientation")!.Value.Should().Be("portrait");

        var ignored = activity.Attribute(tools + "ignore")!.Value.Split(',');
        ignored.Should().Contain("DiscouragedApi");
        ignored.Should().Contain("LockedOrientationActivity");
    }

    /// <summary>Build settings come from the config and the toolchain, not the shell.</summary>
    [Fact]
    public async Task GradleScriptCarriesConfigAndToolchainValues()
    {
        var sink = await GenerateAsync("edge-hostile-text.json");
        var gradle = sink.Text("app/build.gradle.kts");

        gradle.Should().Contain("applicationId = \"com.bobs.diner\"");
        gradle.Should().Contain("compileSdk = 36");
        gradle.Should().Contain("minSdk = 24");
    }

    /// <summary>The project name is a slug, never the raw app name.</summary>
    /// <remarks>
    /// ⚠️ Gradle reads several characters in <c>rootProject.name</c> as path or
    /// task-path syntax, so "@Bob's \"Diner\" &amp; Grill &lt;$5" would produce a
    /// project that fails to configure before a single file is compiled.
    /// </remarks>
    [Fact]
    public async Task ProjectNameIsSlugged()
    {
        var sink = await GenerateAsync("edge-hostile-text.json");

        sink.Text("settings.gradle.kts").Should().Contain("rootProject.name = \"bob-s-diner-grill-5\"");
    }

    /// <summary>The generation manifest records every input that changes a binary.</summary>
    [Fact]
    public async Task GenerationManifestRecordsItsInputs()
    {
        var sink = await GenerateAsync("maximal.json");
        var manifest = JsonNode.Parse(sink.Text(".shellwright/manifest.json"))!;

        manifest["platform"]!.GetValue<string>().Should().Be("android");
        manifest["shellVersion"]!.GetValue<string>().Should().Be(ToolchainDescriptor.Android.ShellVersion);
        manifest["toolchain"]!["agp"]!.GetValue<string>().Should().Be("8.9.0");
        manifest["hashes"]!["codeKey"]!.GetValue<string>().Should().HaveLength(64);
        manifest["hashes"]!["assetKey"]!.GetValue<string>().Should().HaveLength(64);
        manifest["hashes"]!["contentKey"]!.GetValue<string>().Should().HaveLength(64);
    }

    /// <summary>The embedded config is the canonical resolved one.</summary>
    /// <remarks>
    /// The shell reads this at runtime, so it must be the config with defaults
    /// applied — not what the customer typed — and canonical, so that the same
    /// config always produces the same asset bytes.
    /// </remarks>
    [Fact]
    public async Task EmbeddedConfigIsCanonicalAndResolved()
    {
        var sink = await GenerateAsync("minimal.json");
        var embedded = sink.Text("app/src/main/assets/appconfig.json");

        embedded.Should().Be(
            ConfigSchema.CanonicalJson.Serialize(Fixtures.Resolve("minimal.json")) + "\n");

        // A default the customer never typed, proving resolution happened.
        JsonNode.Parse(embedded)!["webOverrides"]!["pullToRefresh"].Should().NotBeNull();
    }

    /// <summary>Every generated XML file parses.</summary>
    /// <remarks>
    /// The cheapest possible check that escaping did not produce something that
    /// merely looks like XML. It runs against the hostile fixture, which is the
    /// one designed to break it.
    /// </remarks>
    [Theory]
    [InlineData("minimal.json")]
    [InlineData("maximal.json")]
    [InlineData("unicode.json")]
    [InlineData("edge-hostile-text.json")]
    [InlineData("edge-portrait-locked.json")]
    public async Task AllGeneratedXmlParses(string fixture)
    {
        var sink = await GenerateAsync(fixture);

        foreach (var file in sink.Files.Where(f => f.Path.EndsWith(".xml", StringComparison.Ordinal)))
        {
            var act = () => XDocument.Parse(sink.Text(file.Path));
            act.Should().NotThrow("{0} should be well-formed XML", file.Path);
        }
    }

    /// <summary>TC-S04-PRF-001 — the maximal fixture generates in under 3 seconds.</summary>
    [Fact]
    public async Task MaximalFixtureGeneratesInsideTheBudget()
    {
        var resolved = Fixtures.Resolve("maximal.json");
        var generator = Fixtures.Generator();

        // Warm the template read and the JIT; the budget is about steady-state
        // generation, which is what a build runner actually does.
        await generator.GenerateAsync(resolved, ToolchainDescriptor.Android, new InMemoryFileSink());

        var stopwatch = Stopwatch.StartNew();
        await generator.GenerateAsync(resolved, ToolchainDescriptor.Android, new InMemoryFileSink());
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(3),
            "generation is on the critical path of every build");
    }
}
