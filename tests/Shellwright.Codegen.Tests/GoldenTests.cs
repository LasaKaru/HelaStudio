using FluentAssertions;
using Shellwright.Codegen;
using Shellwright.Tools.ApproveGolden;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>
/// The approved snapshots of generated projects.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism that stops a one-line template edit silently changing
/// every customer's app. It is only worth anything if the diffs are read, which
/// is why the corpus is six fixtures rather than twenty-nine: a snapshot review
/// nobody performs is worse than none, because it looks like review.
/// </para>
/// <para>
/// Regenerate with <c>dotnet run --project tools/ApproveGolden</c>. ⚠️ Running
/// that is not approval — approval is a person reading the diff it produces.
/// </para>
/// </remarks>
public sealed class GoldenTests
{
    /// <summary>Every fixture, for every platform, has an approved snapshot.</summary>
    public static TheoryData<string, string> Corpus()
    {
        var data = new TheoryData<string, string>();

        foreach (var platform in GoldenCorpus.Platforms)
        {
            foreach (var fixture in GoldenCorpus.Fixtures)
            {
                data.Add(platform, fixture);
            }
        }

        return data;
    }

    /// <summary>TC-S04-GEN-003 — the tree matches its approved snapshot.</summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task TreeMatchesTheApprovedSnapshot(string platform, string fixture)
    {
        var sink = await GoldenCorpus.GenerateAsync(Fixtures.RepoRoot, fixture, platform);
        var name = Path.GetFileNameWithoutExtension(fixture);
        var approved = Path.Combine(Fixtures.GoldenDir, platform, name, "tree.txt");

        File.Exists(approved).Should().BeTrue(
            "no approved snapshot for {0}/{1}. Run: dotnet run --project tools/ApproveGolden",
            platform,
            name);

        var actual = GoldenCorpus.TreeManifest(sink);
        var expected = await File.ReadAllTextAsync(approved);

        actual.Should().Be(
            expected,
            "the generated tree for {0}/{1} changed. Review the diff, then approve it with "
            + "`dotnet run --project tools/ApproveGolden` — never the other way round.",
            platform,
            name);
    }

    /// <summary>TC-S04-GEN-031 — every reviewable file matches, byte for byte.</summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public async Task ReviewableFilesMatchTheirApprovedContent(string platform, string fixture)
    {
        var sink = await GoldenCorpus.GenerateAsync(Fixtures.RepoRoot, fixture, platform);
        var name = Path.GetFileNameWithoutExtension(fixture);
        var root = Path.Combine(Fixtures.GoldenDir, platform, name, "files");

        foreach (var file in sink.Files.Where(file => GoldenCorpus.IsReviewableText(file.Path)))
        {
            var approved = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(approved).Should().BeTrue(
                "{0}/{1}/{2} has no approved content", platform, name, file.Path);

            (await File.ReadAllBytesAsync(approved))
                .Should().Equal(file.Content, "{0}/{1}/{2} changed", platform, name, file.Path);
        }
    }

    /// <summary>
    /// TC-S04-GEN-032 — a template edit is visible as a snapshot failure.
    /// </summary>
    /// <remarks>
    /// The corpus is only a safety net if it actually catches something. This
    /// simulates the change it exists to catch — an edited app name — and
    /// asserts the snapshot no longer matches. Without this, a corpus that had
    /// silently stopped comparing anything would still show green.
    /// </remarks>
    [Fact]
    public async Task AChangedConfigBreaksTheSnapshot()
    {
        var resolved = Fixtures.Resolve("minimal.json");
        resolved["app"]!["name"] = "Something Else";

        var sink = new InMemoryFileSink();
        await Fixtures.Generator().GenerateAsync(resolved, ToolchainDescriptor.Android, sink);

        var approved = await File.ReadAllTextAsync(
            Path.Combine(Fixtures.GoldenDir, "android", "minimal", "tree.txt"));

        GoldenCorpus.TreeManifest(sink).Should().NotBe(approved);
    }
}
