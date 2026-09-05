using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Scriban.Runtime;
using Shellwright.Codegen.Assets;
using Shellwright.Codegen.Normalisation;
using Shellwright.Codegen.Templating;
using Shellwright.ConfigSchema;

namespace Shellwright.Codegen;

/// <summary>
/// Everything both platform generators do the same way.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Extracted before the second generator existed, deliberately. The whole
/// of Sprint 04 is an argument against forking a pipeline: two copies of the
/// render loop would agree for about a sprint, and then Android would get a
/// determinism fix that iOS silently did not. Every rule that keeps output
/// byte-identical — sorted files, LF endings, explicit permission bits, no
/// timestamps, a duplicate path being an error — lives here once.
/// </para>
/// <para>
/// A subclass supplies what is genuinely platform-specific: which escaping each
/// template needs, what extra values the templates can read, and what files the
/// platform generates rather than renders.
/// </para>
/// </remarks>
public abstract class ProjectGenerator : IProjectGenerator
{
    private readonly TemplateSource templates;

    /// <summary>Creates a generator over a shell template tree.</summary>
    /// <param name="templates">The shell to render.</param>
    /// <param name="assets">Where <c>asset://</c> references are resolved.</param>
    /// <param name="images">The icon-resizing pipeline.</param>
    protected ProjectGenerator(
        TemplateSource templates,
        IAssetStore assets,
        IImagePipeline? images = null)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(assets);

        this.templates = templates;
        Assets = assets;
        Images = images ?? new SkiaImagePipeline();
    }

    /// <summary>Where <c>asset://</c> references are resolved.</summary>
    protected IAssetStore Assets { get; }

    /// <summary>The icon-resizing pipeline.</summary>
    protected IImagePipeline Images { get; }

    /// <summary>The platform name recorded in the generation manifest.</summary>
    protected abstract string Platform { get; }

    /// <summary>Where the resolved config is embedded for the shell to read.</summary>
    protected abstract string ConfigAssetPath { get; }

    /// <summary>
    /// Which escaping each template needs, matched most-specific-first.
    /// </summary>
    /// <remarks>
    /// ⚠️ A file extension is not enough to decide. On Android
    /// <c>strings.xml</c> and <c>AndroidManifest.xml</c> are both XML but only
    /// one is subject to the resource compiler's backslash rules. An unlisted
    /// template is a hard error rather than a quiet fall back to no escaping —
    /// a permissive default is a bug waiting for the first template somebody
    /// adds in a hurry.
    /// </remarks>
    protected abstract ImmutableArray<(string Suffix, TemplateFormat Format)> FormatsByPath { get; }

    /// <summary>Template paths the generator writes itself and must not copy.</summary>
    protected virtual ImmutableArray<string> AlwaysGenerated => [ConfigAssetPath, ManifestPath];

    /// <summary>Where the generation manifest is written.</summary>
    protected const string ManifestPath = ".shellwright/manifest.json";

    /// <summary>Values templates can read beyond the config itself.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <param name="toolchain">Tool versions the output depends on.</param>
    /// <returns>Named values, escaped for the target format by the model.</returns>
    protected abstract IReadOnlyDictionary<string, object?> ExtraValues(
        JsonObject resolved,
        ToolchainDescriptor toolchain);

    /// <summary>Files the platform produces rather than renders, such as icons.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <returns>The extra files.</returns>
    protected virtual ImmutableArray<GeneratedFile> PlatformFiles(JsonObject resolved) => [];

    /// <summary>
    /// Path prefixes that belong to the shell but not to a generated project.
    /// </summary>
    /// <remarks>
    /// ⚠️ The shell's own test suite, above all. It is written against
    /// <c>tests/fixtures/</c>, which does not exist in a customer's project, so
    /// it could not run there even if anyone wanted it to — and a customer
    /// opening their exported source to find someone else's tests is being
    /// handed confusion, not value.
    ///
    /// This applies only to generated output. The shell keeps its tests; that
    /// is the whole point of the shell being a real app.
    /// </remarks>
    protected virtual ImmutableArray<string> ExcludedFromGenerated => [];

    /// <summary>Copied template paths this config makes redundant.</summary>
    /// <param name="resolved">A resolved configuration.</param>
    /// <returns>Paths to drop.</returns>
    protected virtual ImmutableArray<string> Superseded(JsonObject resolved) => [];

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(
        JsonObject resolved,
        ToolchainDescriptor toolchain,
        IFileSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(toolchain);
        ArgumentNullException.ThrowIfNull(sink);

        var hashes = ConfigHasher.Compute(resolved, toolchain.ToHashContext());
        var superseded = Superseded(resolved);
        var written = new List<GeneratedFile>();

        foreach (var template in templates.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (AlwaysGenerated.Contains(template.OutputPath)
                || superseded.Contains(template.OutputPath)
                || ExcludedFromGenerated.Any(prefix =>
                    template.OutputPath.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            written.Add(
                template.IsTemplate
                    ? Render(template, resolved, toolchain)
                    : new GeneratedFile(
                        template.OutputPath,

                        // Copied files need the same line-ending guarantee as
                        // rendered ones, or the output encodes whichever
                        // checkout settings the generator happened to run
                        // under. See TextNormaliser.NormaliseCopiedFile.
                        [.. TextNormaliser.NormaliseCopiedFile(template.Content.AsSpan().ToArray())],
                        template.Mode));
        }

        written.Add(new GeneratedFile(
            ConfigAssetPath,
            [.. TextNormaliser.ToBytes(CanonicalJson.Serialize(resolved))]));

        written.Add(Manifest(resolved, toolchain, hashes));
        written.AddRange(PlatformFiles(resolved));

        // Sorted once, here, so the tree manifest and the sink agree and
        // neither depends on the order the rules happened to run in.
        written.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

        foreach (var file in written)
        {
            await sink.WriteAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var tree = written
            .Select(file => new TreeEntry(file.Path, file.Mode, file.Length, HashBytes(file.Content.AsSpan())))
            .ToImmutableArray();

        return new GenerationResult(tree, hashes, HashTree(tree));
    }

    private GeneratedFile Render(TemplateFile template, JsonObject resolved, ToolchainDescriptor toolchain)
    {
        var format = FormatFor(template.OutputPath);

        var extras = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["toolchain"] = ToolchainModel(toolchain),
        };

        foreach (var (key, value) in ExtraValues(resolved, toolchain))
        {
            extras[key] = value;
        }

        var rendered = ScribanTemplateEngine.Render(
            template.OutputPath,
            Encoding.UTF8.GetString(template.Content.AsSpan()),
            TemplateModel.Build(resolved, format, extras));

        return new GeneratedFile(template.OutputPath, [.. TextNormaliser.ToBytes(rendered)], template.Mode);
    }

    private TemplateFormat FormatFor(string outputPath)
    {
        foreach (var (suffix, format) in FormatsByPath)
        {
            if (outputPath.EndsWith(suffix, StringComparison.Ordinal))
            {
                return format;
            }
        }

        throw new TemplateException(
            outputPath,
            "no escaping rule is registered for this path. Add one to FormatsByPath — defaulting to "
            + "no escaping would let a customer's app name break their build, or worse.");
    }

    private static ScriptObject ToolchainModel(ToolchainDescriptor toolchain)
    {
        var script = new ScriptObject();

        foreach (var (key, value) in toolchain.Versions)
        {
            script[key] = value;
        }

        script["shellVersion"] = toolchain.ShellVersion;
        script["generatorVersion"] = toolchain.GeneratorVersion;
        return script;
    }

    /// <summary>
    /// The generation manifest.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not optional. Without it, "why does this customer's app behave
    /// differently from that one?" has no answer but guesswork.
    ///
    /// It carries no timestamp, deliberately: a generated-at field would make
    /// every project differ from every other and destroy the byte-identity the
    /// cache depends on. The build record knows the time already.
    /// </remarks>
    private GeneratedFile Manifest(JsonObject resolved, ToolchainDescriptor toolchain, ConfigHashes hashes)
    {
        var versions = new JsonObject();

        foreach (var (key, value) in toolchain.Versions)
        {
            versions[key] = value;
        }

        var manifest = new JsonObject
        {
            ["generatorVersion"] = toolchain.GeneratorVersion,
            ["shellVersion"] = toolchain.ShellVersion,
            ["platform"] = Platform,
            ["schemaVersion"] = resolved["schemaVersion"]?.DeepClone(),
            ["toolchain"] = versions,
            ["hashes"] = new JsonObject
            {
                ["codeKey"] = hashes.CodeKey,
                ["assetKey"] = hashes.AssetKey,
                ["contentKey"] = hashes.ContentKey,
            },
        };

        return new GeneratedFile(ManifestPath, [.. TextNormaliser.ToBytes(CanonicalJson.Serialize(manifest))]);
    }

    /// <summary>
    /// A project name derived from the app name, safe for a build tool.
    /// </summary>
    /// <remarks>
    /// ⚠️ Neither Gradle nor Xcode will take a customer's app name directly.
    /// Gradle reads several characters in <c>rootProject.name</c> as path or
    /// task-path syntax; Xcode uses the project name in file paths and scheme
    /// names. "My App: 2.0" produces a project that fails to configure in one
    /// and a scheme nobody can select in the other.
    ///
    /// Shared rather than duplicated per platform: two slug functions would
    /// drift, and a customer whose Android and iOS projects were named
    /// differently would be a confusing bug to chase.
    /// </remarks>
    /// <param name="resolved">A resolved configuration.</param>
    /// <param name="separator">What to put between words.</param>
    /// <returns>A safe, non-empty name.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Lowercase is the required output, not a case-folding step for comparison. "
            + "The value is a build-tool identifier and is never compared.")]
    protected static string Slug(JsonObject resolved, char separator = '-')
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var name = resolved["app"]?["name"]?.GetValue<string>() ?? string.Empty;
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != separator)
            {
                builder.Append(separator);
            }
        }

        var slug = builder.ToString().Trim(separator);

        if (slug.Length > 0)
        {
            return slug;
        }

        // A name written entirely in a non-Latin script slugs to nothing, and
        // an empty project name breaks both build tools. The bundle id's last
        // segment is validated ASCII and is always present.
        var bundleId = resolved["app"]?["bundleId"]?.GetValue<string>() ?? "app";
        return bundleId[(bundleId.LastIndexOf('.') + 1)..];
    }

    private static string HashBytes(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(global::Blake3.Hasher.Hash(content).AsSpan());

    private static string HashTree(ImmutableArray<TreeEntry> tree)
    {
        var lines = new StringBuilder();

        foreach (var entry in tree)
        {
            lines.Append(CultureInfo.InvariantCulture, $"{entry.Path} {(int)entry.Mode} {entry.Hash}\n");
        }

        return HashBytes(Encoding.UTF8.GetBytes(lines.ToString()));
    }
}
