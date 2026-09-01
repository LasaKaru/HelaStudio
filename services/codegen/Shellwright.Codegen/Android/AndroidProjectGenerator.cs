using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Scriban.Runtime;
using Shellwright.Codegen.Normalisation;
using Shellwright.Codegen.Templating;
using Shellwright.ConfigSchema;

namespace Shellwright.Codegen.Android;

/// <summary>
/// Renders <c>shells/android</c> into a buildable Gradle project.
/// </summary>
/// <remarks>
/// ⚠️ The governing constraint is <b>byte-identical output for identical
/// input</b>. Without it the three-way build cache (ADR 0004) never hits, and
/// the unit economics the whole business plan rests on do not hold. Sorted
/// collections, an invariant culture, no timestamps in hashed files, and
/// explicit permission bits all exist for that one reason — and each is cheap
/// only because it was done from the start.
/// </remarks>
public sealed class AndroidProjectGenerator : IProjectGenerator
{
    /// <summary>
    /// Which escaping each template needs, matched most-specific-first.
    /// </summary>
    /// <remarks>
    /// ⚠️ Explicit, because a file extension is not enough to decide:
    /// <c>strings.xml</c> and <c>AndroidManifest.xml</c> are both XML, but only
    /// one is subject to the resource compiler's backslash rules. An unlisted
    /// template is a hard error rather than a quiet fall back to no escaping —
    /// a permissive default here is a bug waiting for the first template
    /// somebody adds in a hurry.
    /// </remarks>
    private static readonly ImmutableArray<(string Suffix, TemplateFormat Format)> FormatsByPath =
        [
            ("strings.xml", TemplateFormat.AndroidResource),
            (".xml", TemplateFormat.Xml),
            (".kts", TemplateFormat.GradleKotlin),
            (".gradle", TemplateFormat.GradleKotlin),
            (".json", TemplateFormat.Json),
            (".pro", TemplateFormat.None),
            (".toml", TemplateFormat.None),
            (".properties", TemplateFormat.None),
        ];

    /// <summary>
    /// Paths the generator always writes itself, and so must not copy.
    /// </summary>
    /// <remarks>
    /// The shell keeps a real <c>appconfig.json</c> so it runs standalone —
    /// that file is what the app reads on a developer's device. For a generated
    /// project the customer's config takes its place. Copying it as well would
    /// emit the path twice, which the sink rejects; the sink caught this on the
    /// generator's very first run, which is the argument for having made a
    /// duplicate path an error rather than a silent overwrite.
    /// </remarks>
    private static readonly ImmutableArray<string> AlwaysGenerated =
        [
            "app/src/main/assets/appconfig.json",
            ".shellwright/manifest.json",
        ];

    private readonly TemplateSource templates;

    /// <summary>Creates a generator reading from a shell template tree.</summary>
    /// <param name="templates">The shell to render.</param>
    public AndroidProjectGenerator(TemplateSource templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        this.templates = templates;
    }

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
        var written = new List<GeneratedFile>();

        foreach (var template in templates.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (AlwaysGenerated.Contains(template.OutputPath))
            {
                continue;
            }

            written.Add(
                template.IsTemplate
                    ? Render(template, resolved, toolchain)
                    : new GeneratedFile(template.OutputPath, template.Content, template.Mode));
        }

        written.Add(ConfigAsset(resolved));
        written.Add(Manifest(resolved, toolchain, hashes));

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

    private static GeneratedFile Render(
        TemplateFile template,
        JsonObject resolved,
        ToolchainDescriptor toolchain)
    {
        var format = FormatFor(template.OutputPath);

        var model = TemplateModel.Build(
            resolved,
            format,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["toolchain"] = ToolchainModel(toolchain),
                ["locales"] = Locales(resolved),
                ["deepLinkHosts"] = DeepLinkHosts(resolved),
                ["grantedPermissions"] = GrantedPermissions(resolved),
                ["removedPermissions"] = RemovedPermissions(resolved),
                ["projectSlug"] = ProjectSlug(resolved),
                ["customScheme"] = resolved["deepLinks"]?["customScheme"]?.GetValue<string>() ?? string.Empty,
                ["screenOrientation"] = ScreenOrientation(resolved),
            });

        var rendered = ScribanTemplateEngine.Render(
            template.OutputPath,
            Encoding.UTF8.GetString(template.Content.AsSpan()),
            model);

        return new GeneratedFile(template.OutputPath, [.. TextNormaliser.ToBytes(rendered)], template.Mode);
    }

    private static TemplateFormat FormatFor(string outputPath)
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

    /// <summary>The locales the app ships resources for, always including one.</summary>
    private static List<string> Locales(JsonObject resolved)
    {
        var locales = (resolved["localization"]?["locales"] as JsonArray)
            ?.Select(node => node!.GetValue<string>())
            .ToList() ?? [];

        if (locales.Count == 0)
        {
            locales.Add("en");
        }

        // Sorted so resourceConfigurations is stable whatever order the
        // customer listed their languages in. Unsorted, a mere reorder would
        // read to the cache as a code change and force a full recompile.
        locales.Sort(StringComparer.Ordinal);
        return locales;
    }

    /// <summary>App Links hosts, sorted and deduplicated.</summary>
    private static List<string> DeepLinkHosts(JsonObject resolved)
    {
        var hosts = new SortedSet<string>(StringComparer.Ordinal);

        if (resolved["deepLinks"]?["universalLinks"] is JsonArray links)
        {
            // The schema types these as bare hostname strings, not objects.
            foreach (var link in links)
            {
                if (link is JsonValue value
                    && value.TryGetValue<string>(out var host)
                    && host.Length > 0)
                {
                    hosts.Add(host);
                }
            }
        }

        return [.. hosts];
    }

    /// <summary>
    /// Android permission names the config actually justifies.
    /// </summary>
    /// <remarks>
    /// Everything not listed is stripped with <c>tools:node="remove"</c>. An
    /// unjustified permission is among the most common store rejection reasons,
    /// which is why Sprint 01 warns about it (<c>CFG_PERMISSION_UNJUSTIFIED</c>)
    /// and why the generator <i>removes</i> rather than merely omits: a library
    /// that declares one on the app's behalf must not reintroduce it during
    /// manifest merging.
    /// </remarks>
    private static readonly ImmutableArray<string> OptionalPermissions =
        [
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.CAMERA",
            "android.permission.POST_NOTIFICATIONS",
            "android.permission.RECORD_AUDIO",
        ];

    /// <summary>Optional permissions the config does not justify, sorted.</summary>
    private static List<string> RemovedPermissions(JsonObject resolved)
    {
        var granted = GrantedPermissions(resolved);
        return [.. OptionalPermissions.Where(name => !granted.Contains(name))];
    }

    /// <summary>
    /// A Gradle project name derived from the app name.
    /// </summary>
    /// <remarks>
    /// ⚠️ Gradle treats several characters in <c>rootProject.name</c> as path
    /// separators or task-path syntax, so a customer's app name cannot be used
    /// directly — "My App: 2.0" would produce a project that fails to
    /// configure. Deriving a slug keeps the name recognisable in build output
    /// while guaranteeing it is safe, and falls back to the bundle id's last
    /// segment when a name is entirely non-Latin, which a slug of it would
    /// otherwise reduce to nothing.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Lowercase is the required output, not a case-folding step for comparison. "
            + "Gradle project names are conventionally lowercase, and this value is never compared.")]
    private static string ProjectSlug(JsonObject resolved)
    {
        var name = resolved["app"]?["name"]?.GetValue<string>() ?? string.Empty;
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > 0)
        {
            return slug;
        }

        var bundleId = resolved["app"]?["bundleId"]?.GetValue<string>() ?? "app";
        return bundleId[(bundleId.LastIndexOf('.') + 1)..];
    }

    /// <summary>
    /// The <c>android:screenOrientation</c> value, or empty to omit the attribute.
    /// </summary>
    /// <remarks>
    /// ⚠️ Empty rather than <c>"unspecified"</c> for the "any" case, and the
    /// template omits the attribute entirely. They behave identically at
    /// runtime, but Android lint flags <i>any</i> use of the attribute
    /// (<c>DiscouragedApi</c>) because a fixed orientation letterboxes the app
    /// on tablets and foldables — and lint runs with warnings as errors in
    /// every generated project, so emitting a redundant attribute would fail a
    /// customer's build for nothing.
    ///
    /// When a customer does ask for a fixed orientation, the template suppresses
    /// the check on that one attribute. Their explicit choice is not something
    /// their build should refuse.
    /// </remarks>
    private static string ScreenOrientation(JsonObject resolved) =>
        resolved["build"]?["orientation"]?.GetValue<string>() switch
        {
            "portrait" => "portrait",
            "landscape" => "landscape",
            _ => string.Empty,
        };

    private static List<string> GrantedPermissions(JsonObject resolved)
    {
        var permissions = resolved["permissions"] as JsonObject;
        var granted = new SortedSet<string>(StringComparer.Ordinal);

        if (permissions?["camera"]?.GetValue<bool>() == true)
        {
            granted.Add("android.permission.CAMERA");
        }

        if (permissions?["microphone"]?.GetValue<bool>() == true)
        {
            granted.Add("android.permission.RECORD_AUDIO");
        }

        if (permissions?["notifications"]?.GetValue<bool>() == true)
        {
            granted.Add("android.permission.POST_NOTIFICATIONS");
        }

        if (permissions?["location"]?.GetValue<string>() is "whenInUse" or "always")
        {
            granted.Add("android.permission.ACCESS_FINE_LOCATION");
        }

        return [.. granted];
    }

    /// <summary>The resolved config, canonical, as the shell reads it at runtime.</summary>
    private static GeneratedFile ConfigAsset(JsonObject resolved) =>
        new(
            "app/src/main/assets/appconfig.json",
            [.. TextNormaliser.ToBytes(CanonicalJson.Serialize(resolved))]);

    /// <summary>
    /// The generation manifest.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not optional. Without it, "why does this customer's app behave
    /// differently from that one?" has no answer but guesswork. It records
    /// every input that could produce a different binary.
    ///
    /// It carries no timestamp, deliberately: a generated-at field would make
    /// every project differ from every other and destroy the byte-identity the
    /// cache depends on. The build record knows the time already.
    /// </remarks>
    private static GeneratedFile Manifest(
        JsonObject resolved,
        ToolchainDescriptor toolchain,
        ConfigHashes hashes)
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
            ["platform"] = "android",
            ["schemaVersion"] = resolved["schemaVersion"]?.DeepClone(),
            ["toolchain"] = versions,
            ["hashes"] = new JsonObject
            {
                ["codeKey"] = hashes.CodeKey,
                ["assetKey"] = hashes.AssetKey,
                ["contentKey"] = hashes.ContentKey,
            },
        };

        return new GeneratedFile(
            ".shellwright/manifest.json",
            [.. TextNormaliser.ToBytes(CanonicalJson.Serialize(manifest))]);
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
