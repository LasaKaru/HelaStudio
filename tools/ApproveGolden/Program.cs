using Shellwright.Codegen;
using Shellwright.Codegen.Android;
using Shellwright.Tools.ApproveGolden;

// Regenerates everything the codegen tests assert against:
//
//   1. The committed Android shell files that have a .tmpl sibling. This is
//      what keeps one Android codebase from becoming two — the shell you can
//      open in Android Studio is literally the rendering of its own templates.
//   2. The approved snapshots for the golden corpus.
//
// ⚠️ Running this is not approval. Approval is a person reading the diff it
// produces. A snapshot corpus nobody reads is worse than none, because it looks
// like review; that is why the pull-request checklist asks for the diff
// explicitly.
var repoRoot = FindRepoRoot();

Console.WriteLine($"Repository: {repoRoot}");

// `emit <fixture> <directory>` writes one project to disk instead. This is what
// the nightly real-build job uses: golden files prove the generator is stable,
// they do not prove its output compiles, and only a real Gradle build does that.
if (args is ["emit", var fixture, var outputDirectory, ..])
{
    var platform = args.Length > 3 ? args[3] : "android";
    var sink = await GoldenCorpus.GenerateAsync(repoRoot, fixture, platform).ConfigureAwait(false);
    var target = new DirectoryFileSink(outputDirectory);

    foreach (var file in sink.Files)
    {
        await target.WriteAsync(file).ConfigureAwait(false);
    }

    Console.WriteLine($"Wrote {sink.Files.Count} {platform} files for {fixture} to {outputDirectory}");
    return 0;
}

await RegenerateShellAsync(repoRoot, "android").ConfigureAwait(false);
await RegenerateShellAsync(repoRoot, "ios").ConfigureAwait(false);
await RegenerateGoldensAsync(repoRoot).ConfigureAwait(false);

Console.WriteLine();
Console.WriteLine("Done. Review `git diff` before committing — that review is the point.");

return 0;

static async Task RegenerateShellAsync(string repoRoot, string platform)
{
    var shell = Path.Combine(repoRoot, "shells", platform);
    var configPath = platform == "android"
        ? Path.Combine(shell, "app", "src", "main", "assets", "appconfig.json")
        : Path.Combine(shell, "Resources", "appconfig.json");
    var resolved = GoldenCorpus.Resolve(
        System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(configPath).ConfigureAwait(false))!);

    var sink = new InMemoryFileSink();
    var assets = GoldenCorpus.AssetStore(repoRoot);

    ProjectGenerator generator = platform == "android"
        ? new AndroidProjectGenerator(new TemplateSource(shell), assets)

        // ⚠️ The shell renders with its own tests kept. A generated project does
        // not: see IosProjectGenerator.ExtraValues. This is the one place the
        // shell and a generated project legitimately differ, and it is why the
        // shell can still be the template — it is the same tree with one flag
        // flipped, not a fork.
        : new Shellwright.Codegen.Ios.IosProjectGenerator(
            new TemplateSource(shell), assets, includeTests: true);

    var toolchain = platform == "android" ? ToolchainDescriptor.Android : ToolchainDescriptor.Ios;

    await generator.GenerateAsync(resolved, toolchain, sink).ConfigureAwait(false);

    Console.WriteLine();
    Console.WriteLine($"{platform}: shell files rendered from their own templates:");

    foreach (var file in sink.Files)
    {
        var templatePath = Path.Combine(
            shell,
            TemplateSource.TemplateDirectory,
            file.Path.Replace('/', Path.DirectorySeparatorChar)) + TemplateSource.TemplateSuffix;

        // Only files that actually have a template are written back. The rest
        // of the shell is ordinary source and is copied, not generated.
        if (!File.Exists(templatePath))
        {
            continue;
        }

        var target = Path.Combine(shell, file.Path.Replace('/', Path.DirectorySeparatorChar));
        await File.WriteAllBytesAsync(target, file.Content.AsSpan().ToArray()).ConfigureAwait(false);
        Console.WriteLine($"  {file.Path}");
    }
}

static async Task RegenerateGoldensAsync(string repoRoot)
{
    var goldenRoot = Path.Combine(repoRoot, "tests", "fixtures", "generated");

    if (Directory.Exists(goldenRoot))
    {
        Directory.Delete(goldenRoot, recursive: true);
    }

    Console.WriteLine();
    Console.WriteLine("Golden snapshots:");

    foreach (var platform in GoldenCorpus.Platforms)
    {
        foreach (var fixture in GoldenCorpus.Fixtures)
        {
            var sink = await GoldenCorpus.GenerateAsync(repoRoot, fixture, platform).ConfigureAwait(false);
            var name = Path.GetFileNameWithoutExtension(fixture);
            var directory = Path.Combine(goldenRoot, platform, name);

            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "tree.txt"),
                GoldenCorpus.TreeManifest(sink)).ConfigureAwait(false);

            var reviewable = 0;

            foreach (var file in sink.Files.Where(file => GoldenCorpus.IsReviewableText(file.Path)))
            {
                var target = Path.Combine(directory, "files", file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, file.Content.AsSpan().ToArray()).ConfigureAwait(false);
                reviewable++;
            }

            Console.WriteLine($"  {platform}/{name}: {sink.Files.Count} files, {reviewable} committed in full");
        }
    }
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate the repository root.");
}
