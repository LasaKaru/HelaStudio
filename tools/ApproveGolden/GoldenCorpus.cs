using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Shellwright.Codegen;
using Shellwright.Codegen.Android;
using Shellwright.ConfigSchema;

namespace Shellwright.Tools.ApproveGolden;

/// <summary>
/// Produces and locates the approved snapshots of generated projects.
/// </summary>
/// <remarks>
/// Shared by the approval tool and by the golden tests so that "what CI checks"
/// and "what the tool writes" cannot diverge — the one failure that would make
/// an approval workflow worse than no approval workflow at all.
/// </remarks>
public static class GoldenCorpus
{
    /// <summary>The fixtures a full project is generated and approved for.</summary>
    /// <remarks>
    /// ⚠️ Deliberately small. The sprint plan names an unreviewed snapshot
    /// corpus as a high-likelihood risk: a diff nobody reads is worse than no
    /// diff, because it looks like review. Five fixtures spanning the shapes
    /// that break codegen — nothing, everything, non-Latin text, many tabs,
    /// many rules — stay readable in a pull request.
    /// </remarks>
    public static ImmutableArray<string> Fixtures { get; } =
        [
            "minimal.json",
            "maximal.json",
            "unicode.json",
            "edge-many-tabs.json",
            "edge-many-linkrules.json",

            // Every character that breaks a generated Android build, in one
            // app name: a leading @, an apostrophe, quotes, an ampersand, a
            // less-than, and a Kotlin $. Added in Sprint 04 because unicode.json
            // covers scripts but not punctuation, and punctuation is what
            // actually fails aapt2.
            "edge-hostile-text.json",

            // A fixed orientation trips two Android lint checks rather than
            // one, and no other fixture sets one. Added when a generated
            // portrait project failed lint that every unit test had passed.
            "edge-portrait-locked.json",
        ];

    /// <summary>
    /// Text files whose full content is committed rather than only their hash.
    /// </summary>
    /// <remarks>
    /// A hash tells you a file changed. The point of the corpus is to show
    /// <i>how</i>, so a reviewer can see that an app name moved rather than
    /// that a checksum did. Binaries get hashes because their diffs are
    /// unreadable anyway.
    /// </remarks>
    public static bool IsReviewableText(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path.EndsWith(".xml", StringComparison.Ordinal)
        || path.EndsWith(".kts", StringComparison.Ordinal)
        || path.EndsWith(".json", StringComparison.Ordinal)
        || path.EndsWith(".pro", StringComparison.Ordinal)
        || path.EndsWith(".toml", StringComparison.Ordinal)
            || path.EndsWith(".properties", StringComparison.Ordinal);
    }

    /// <summary>Generates one fixture into memory.</summary>
    /// <param name="repoRoot">The repository root.</param>
    /// <param name="fixture">A file name in <c>tests/fixtures/configs</c>.</param>
    /// <returns>The sink holding the generated project.</returns>
    public static async Task<InMemoryFileSink> GenerateAsync(string repoRoot, string fixture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var configPath = Path.Combine(repoRoot, "tests", "fixtures", "configs", fixture);
        var resolved = Resolve(JsonNode.Parse(await File.ReadAllTextAsync(configPath).ConfigureAwait(false))!);

        var sink = new InMemoryFileSink();
        var generator = new AndroidProjectGenerator(
            new TemplateSource(Path.Combine(repoRoot, "shells", "android")));

        await generator.GenerateAsync(resolved, ToolchainDescriptor.Android, sink).ConfigureAwait(false);
        return sink;
    }

    /// <summary>Resolves schema defaults, refusing an invalid config.</summary>
    /// <param name="config">The raw configuration.</param>
    /// <returns>The resolved configuration.</returns>
    public static JsonObject Resolve(JsonNode config)
    {
        var validated = new ConfigValidator().Validate(config);

        return validated.Result.Errors.Length == 0
            ? validated.Resolved
            : throw new InvalidOperationException(
                "Config does not validate: "
                + string.Join("; ", validated.Result.Errors.Select(error => $"{error.Code} at {error.Path}")));
    }

    /// <summary>Renders the tree manifest a snapshot is compared against.</summary>
    /// <param name="sink">A generated project.</param>
    /// <returns>One line per file: mode, size, hash, path.</returns>
    public static string TreeManifest(InMemoryFileSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var lines = new StringBuilder();

        foreach (var file in sink.Files)
        {
            var hash = Convert.ToHexStringLower(global::Blake3.Hasher.Hash(file.Content.AsSpan()).AsSpan());
            lines.Append(file.Mode == FilePermissions.Executable ? "0755 " : "0644 ");
            lines.Append(System.Globalization.CultureInfo.InvariantCulture, $"{file.Length,9} ");
            lines.Append(hash[..16]).Append("  ").Append(file.Path).Append('\n');
        }

        return lines.ToString();
    }
}
