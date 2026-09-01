using System.Collections.Immutable;

namespace Shellwright.Codegen;

/// <summary>One file in a template tree, before rendering.</summary>
/// <param name="OutputPath">Where it lands in a generated project, forward-slashed.</param>
/// <param name="Content">The raw bytes.</param>
/// <param name="Mode">Permission bits to carry through to the output.</param>
/// <param name="IsTemplate">Whether <see cref="Content"/> must be rendered.</param>
public sealed record TemplateFile(
    string OutputPath,
    ImmutableArray<byte> Content,
    FilePermissions Mode,
    bool IsTemplate);

/// <summary>
/// Reads a shell template tree from disk.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The template <i>is</i> the hand-written shell. There is one Android
/// codebase, not two: <c>shells/android</c> is both the app that
/// <c>./gradlew assembleDebug</c> builds and the tree this generator renders.
/// Forking it into a separate template project is the single change most likely
/// to break this system — the fork would drift within a sprint, and the drift
/// would surface only as a customer's build failing on code that works
/// perfectly in the repository.
/// </para>
/// <para>
/// What keeps the two honest is that the committed concrete files are
/// themselves the rendering of the templates against the shell's own
/// <c>assets/appconfig.json</c>, asserted by <c>ShellTemplateTests</c>. Editing
/// a template without regenerating fails CI.
/// </para>
/// <para>
/// ⚠️ Templates live under <c>templates/</c>, mirroring the paths they render
/// to, rather than sitting beside their output. That is not tidiness: the
/// Android resource merger rejects any file under <c>res/</c> whose name does
/// not end in <c>.xml</c>, so a <c>colors.xml.tmpl</c> next to
/// <c>colors.xml</c> breaks the shell's own build. Renaming to
/// <c>colors.tmpl.xml</c> would be worse — the merger would then parse it as a
/// real resource and collide with the file it generates.
/// </para>
/// </remarks>
public sealed class TemplateSource
{
    /// <summary>The suffix marking a file that is rendered rather than copied.</summary>
    public const string TemplateSuffix = ".tmpl";

    /// <summary>The directory templates live in, relative to the shell root.</summary>
    public const string TemplateDirectory = "templates";

    private static readonly ImmutableArray<string> ExcludedDirectories =
        ["build", ".gradle", ".idea", ".kotlin", "DerivedData", ".build"];

    private static readonly ImmutableArray<string> ExcludedFiles =
        [
            // A developer's absolute SDK path. The one file in the shell that
            // must never leave this machine.
            "local.properties",

            // Documentation for people working on the shell, not for the app.
            "README.md",
        ];

    private readonly string root;

    /// <summary>Creates a source reading from <paramref name="root"/>.</summary>
    /// <param name="root">The shell directory, such as <c>shells/android</c>.</param>
    public TemplateSource(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);

        if (!Directory.Exists(this.root))
        {
            throw new DirectoryNotFoundException($"No shell template at '{this.root}'.");
        }
    }

    /// <summary>Every file a generated project gets, ordered by output path.</summary>
    /// <returns>The template files.</returns>
    /// <exception cref="InvalidOperationException">A template has no committed rendering.</exception>
    public ImmutableArray<TemplateFile> Read()
    {
        var templated = new Dictionary<string, TemplateFile>(StringComparer.Ordinal);
        var copied = new Dictionary<string, TemplateFile>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

            if (IsExcluded(relative))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(path);

            if (relative.StartsWith(TemplateDirectory + "/", StringComparison.Ordinal))
            {
                if (!relative.EndsWith(TemplateSuffix, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"'{relative}' is under {TemplateDirectory}/ but is not a {TemplateSuffix} file. "
                        + "Everything there is rendered; nothing else belongs.");
                }

                var output = relative[(TemplateDirectory.Length + 1)..^TemplateSuffix.Length];
                templated[output] = new TemplateFile(
                    output, [.. bytes], FilePermissions.Regular, IsTemplate: true);
            }
            else
            {
                copied[relative] = new TemplateFile(
                    relative,
                    [.. bytes],

                    // gradlew must stay runnable. Everything else is 0644, set
                    // explicitly rather than inherited, because a developer's
                    // umask is not an input the build cache knows about.
                    relative.EndsWith("gradlew", StringComparison.Ordinal)
                        ? FilePermissions.Executable
                        : FilePermissions.Regular,
                    false);
            }
        }

        foreach (var output in templated.Keys)
        {
            // The concrete file next to a template is that template's committed
            // rendering — the reason the shell still opens in Android Studio.
            // Reading it as input as well would claim the same output path
            // twice, which the sink rejects.
            if (!copied.Remove(output))
            {
                throw new InvalidOperationException(
                    $"'{output}' is generated from a template but has no committed rendering. "
                    + "Run: dotnet run --project tools/ApproveGolden");
            }
        }

        // Ordered here rather than at every call site: file order feeds the
        // tree hash, and a hash that depends on directory-enumeration order is
        // a cache that misses on a different filesystem.
        return
        [
            .. templated.Values.Concat(copied.Values)
                .OrderBy(file => file.OutputPath, StringComparer.Ordinal),
        ];
    }

    private static bool IsExcluded(string relativePath)
    {
        var segments = relativePath.Split('/');

        return segments[..^1].Any(ExcludedDirectories.Contains)
            || ExcludedFiles.Contains(segments[^1]);
    }
}
