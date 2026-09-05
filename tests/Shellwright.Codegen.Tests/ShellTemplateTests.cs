using FluentAssertions;
using Shellwright.Codegen;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>
/// The one-codebase rule.
/// </summary>
/// <remarks>
/// ⚠️ The sprint plan names template drift as the highest-likelihood risk in
/// Sprint 04, and it is right: a template forked from the shell looks correct
/// for about a sprint, and then a customer's build fails on code that works
/// perfectly in this repository.
///
/// The defence is that <c>shells/android</c> is both the app and its own
/// template. The committed files are literally the rendering of the templates
/// against the shell's own <c>appconfig.json</c>, and this test is what says so
/// out loud.
/// </remarks>
public sealed class ShellTemplateTests
{
    /// <summary>The committed shell is the rendering of its own templates.</summary>
    [Fact]
    public async Task CommittedShellFilesMatchTheirTemplates()
    {
        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.ShellConfig(), ToolchainDescriptor.Android, sink);

        var checkedFiles = 0;

        foreach (var file in sink.Files)
        {
            var templatePath = Path.Combine(
                Fixtures.AndroidShell,
                TemplateSource.TemplateDirectory,
                file.Path.Replace('/', Path.DirectorySeparatorChar)) + TemplateSource.TemplateSuffix;

            if (!File.Exists(templatePath))
            {
                continue;
            }

            var committed = Path.Combine(
                Fixtures.AndroidShell, file.Path.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(committed).Should().BeTrue(
                "{0} has a template but no committed rendering, so the shell no longer builds standalone",
                file.Path);

            (await File.ReadAllBytesAsync(committed)).Should().Equal(
                file.Content,
                "{0} has drifted from its template. Run: dotnet run --project tools/ApproveGolden",
                file.Path);

            checkedFiles++;
        }

        // A test that silently checks nothing is worse than no test. If the
        // templates are ever renamed or moved, this fails rather than passing
        // vacuously.
        checkedFiles.Should().BeGreaterThan(3, "the shell should have several templated files");
    }

    /// <summary>Every template in the shell has a registered escaping rule.</summary>
    /// <remarks>
    /// The generator refuses to render a template it has no rule for, which
    /// turns "somebody added a template in a hurry" into a build failure rather
    /// than an unescaped value in a customer's project. This test finds that
    /// failure at the moment the template is added, rather than the first time
    /// a fixture happens to exercise it.
    /// </remarks>
    [Fact]
    public async Task EveryTemplateRendersForEveryCorpusFixture()
    {
        var templates = new TemplateSource(Fixtures.AndroidShell).Read()
            .Where(file => file.IsTemplate)
            .Select(file => file.OutputPath)
            .ToList();

        templates.Should().NotBeEmpty();

        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.Resolve("minimal.json"), ToolchainDescriptor.Android, sink);

        foreach (var expected in templates)
        {
            sink.Find(expected).Should().NotBeNull("{0} was not rendered", expected);
        }
    }

    /// <summary>Each output path is claimed exactly once.</summary>
    /// <remarks>
    /// A template's committed rendering must not also be copied as input: both
    /// would claim the same output path. The sink rejects that anyway — it
    /// caught exactly this on the generator's first run — but a rule worth
    /// discovering the hard way is worth naming.
    /// </remarks>
    [Fact]
    public void EachOutputPathIsClaimedOnce()
    {
        var paths = new TemplateSource(Fixtures.AndroidShell).Read()
            .Select(file => file.OutputPath)
            .ToList();

        paths.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// No template file sits where the Android resource merger can see it.
    /// </summary>
    /// <remarks>
    /// ⚠️ AGP rejects any file under <c>res/</c> whose name does not end in
    /// <c>.xml</c>, so a stray <c>colors.xml.tmpl</c> beside its output breaks
    /// the shell's own <c>./gradlew assembleDebug</c> — which is the thing the
    /// one-directory rule exists to protect. That is why templates live under
    /// <c>templates/</c>, and this is the test that says so.
    /// </remarks>
    [Fact]
    public void NoTemplateSitsInsideAnAndroidSourceSet()
    {
        var strays = Directory
            .EnumerateFiles(Fixtures.AndroidShell, "*" + TemplateSource.TemplateSuffix, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Fixtures.AndroidShell, path)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !path.StartsWith(TemplateSource.TemplateDirectory + "/", StringComparison.Ordinal))
            .ToList();

        strays.Should().BeEmpty("templates belong under {0}/", TemplateSource.TemplateDirectory);
    }

    /// <summary>Build directories and developer-local files never reach a project.</summary>
    [Fact]
    public void BuildOutputAndLocalPropertiesAreExcluded()
    {
        var paths = new TemplateSource(Fixtures.AndroidShell).Read()
            .Select(file => file.OutputPath)
            .ToList();

        // ⚠️ local.properties holds a developer's absolute SDK path. It is the
        // one file in the shell that must never leave this machine.
        paths.Should().NotContain("local.properties");
        paths.Should().NotContain(path => path.StartsWith("app/build/", StringComparison.Ordinal));
        paths.Should().NotContain(path => path.Contains("/.gradle/", StringComparison.Ordinal));
        paths.Should().NotContain(path => path.StartsWith("templates/", StringComparison.Ordinal));
    }

    /// <summary>gradlew stays executable; everything else does not.</summary>
    [Fact]
    public void OnlyTheWrapperIsExecutable()
    {
        var files = new TemplateSource(Fixtures.AndroidShell).Read();

        files.Where(file => file.Mode == FilePermissions.Executable)
            .Select(file => file.OutputPath)
            .Should().Equal("gradlew");
    }
}
