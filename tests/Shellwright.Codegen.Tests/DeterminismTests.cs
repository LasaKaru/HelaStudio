using System.Text.Json.Nodes;
using FluentAssertions;
using Shellwright.Codegen;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>
/// The property the whole build cache rests on.
/// </summary>
/// <remarks>
/// ⚠️ Identical input must produce identical bytes. If it does not, the
/// three-way cache key (ADR 0004) never hits, every user-triggered build is a
/// full recompile, and the unit economics in master spec §16 stop working.
/// This runs on every pull request from the moment the generator exists,
/// because nondeterminism found late is nondeterminism that has already been
/// designed around.
/// </remarks>
public sealed class DeterminismTests
{
    /// <summary>TC-S04-GEN-029 — generating twice gives byte-identical output.</summary>
    [Theory]
    [InlineData("minimal.json")]
    [InlineData("maximal.json")]
    [InlineData("unicode.json")]
    [InlineData("edge-hostile-text.json")]
    [InlineData("edge-many-tabs.json")]
    public async Task GeneratingTwiceIsByteIdentical(string fixture)
    {
        var resolved = Fixtures.Resolve(fixture);

        var first = new InMemoryFileSink();
        var second = new InMemoryFileSink();

        var firstResult = await Fixtures.Generator()
            .GenerateAsync(resolved, ToolchainDescriptor.Android, first);
        var secondResult = await Fixtures.Generator()
            .GenerateAsync(resolved, ToolchainDescriptor.Android, second);

        second.Files.Select(file => file.Path)
            .Should().Equal(first.Files.Select(file => file.Path), "the file list must not vary");

        foreach (var expected in first.Files)
        {
            var actual = second.Find(expected.Path);

            actual.Should().NotBeNull();
            actual!.Content.Should().Equal(expected.Content, "{0} differed between two runs", expected.Path);
            actual.Mode.Should().Be(expected.Mode);
        }

        secondResult.TreeHash.Should().Be(firstResult.TreeHash);
    }

    /// <summary>
    /// Reordering keys in the source config changes nothing.
    /// </summary>
    /// <remarks>
    /// A studio that serialises its form state cannot be relied on to emit keys
    /// in a stable order. If order leaked into the output, saving a config
    /// without editing it would invalidate the cache.
    /// </remarks>
    [Fact]
    public async Task KeyOrderInTheSourceConfigDoesNotMatter()
    {
        var original = (JsonObject)JsonNode.Parse(
            await File.ReadAllTextAsync(Path.Combine(Fixtures.ConfigDir, "maximal.json")))!;

        var reversed = new JsonObject();

        foreach (var (key, value) in original.Reverse().ToList())
        {
            reversed[key] = value?.DeepClone();
        }

        var straight = new InMemoryFileSink();
        var flipped = new InMemoryFileSink();

        await Fixtures.Generator().GenerateAsync(
            Fixtures.Resolve(original.DeepClone()), ToolchainDescriptor.Android, straight);
        await Fixtures.Generator().GenerateAsync(
            Fixtures.Resolve(reversed), ToolchainDescriptor.Android, flipped);

        foreach (var expected in straight.Files)
        {
            flipped.Find(expected.Path)!.Content
                .Should().Equal(expected.Content, "{0} depended on key order", expected.Path);
        }
    }

    /// <summary>
    /// A different toolchain changes the manifest and the code key, nothing else.
    /// </summary>
    /// <remarks>
    /// This is the cache split working: bumping the Android Gradle Plugin has
    /// to force a recompile, so it must reach <c>codeKey</c>. It must not reach
    /// <c>contentKey</c>, or a colour change after a toolchain bump would be
    /// billed as a full build.
    /// </remarks>
    [Fact]
    public async Task ToolchainVersionReachesTheCodeKeyOnly()
    {
        var resolved = Fixtures.Resolve("maximal.json");
        var bumped = ToolchainDescriptor.Android with
        {
            Versions = ToolchainDescriptor.Android.Versions.SetItem("agp", "8.10.0"),
        };

        var before = new InMemoryFileSink();
        var after = new InMemoryFileSink();

        var first = await Fixtures.Generator()
            .GenerateAsync(resolved, ToolchainDescriptor.Android, before);
        var second = await Fixtures.Generator()
            .GenerateAsync(resolved, bumped, after);

        second.Hashes.CodeKey.Should().NotBe(first.Hashes.CodeKey);
        second.Hashes.AssetKey.Should().Be(first.Hashes.AssetKey);
        second.Hashes.ContentKey.Should().Be(first.Hashes.ContentKey);
    }

    /// <summary>No generated file carries a timestamp or an absolute path.</summary>
    /// <remarks>
    /// ⚠️ Both are invisible in review and fatal to byte-identity. A
    /// generated-at field in the manifest is the tempting one — it belongs to
    /// the build record, which knows the time already.
    /// </remarks>
    [Fact]
    public async Task NoGeneratedTextCarriesTheBuildEnvironment()
    {
        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.Resolve("maximal.json"), ToolchainDescriptor.Android, sink);

        var manifest = sink.Text(".shellwright/manifest.json");

        manifest.Should().NotContain("generatedAt");
        manifest.Should().NotContain(Fixtures.RepoRoot);

        foreach (var file in sink.Files.Where(file => file.Path.EndsWith(".json", StringComparison.Ordinal)))
        {
            sink.Text(file.Path).Should().NotContain(Fixtures.RepoRoot, "{0} leaked a build path", file.Path);
        }
    }
}
