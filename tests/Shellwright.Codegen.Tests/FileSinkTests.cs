using System.Collections.Immutable;
using System.Text;
using FluentAssertions;
using Shellwright.Codegen;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>The sink's guardrails.</summary>
public sealed class FileSinkTests
{
    private static GeneratedFile File(string path, string content = "x") =>
        new(path, [.. Encoding.UTF8.GetBytes(content)]);

    /// <summary>Two rules claiming one path is an error, not a silent overwrite.</summary>
    /// <remarks>
    /// This caught a real bug on the generator's first run: the shell's own
    /// <c>appconfig.json</c> was both copied as template input and written as
    /// generated output. A last-write-wins sink would have shipped whichever
    /// happened to run second.
    /// </remarks>
    [Fact]
    public async Task DuplicatePathIsRejected()
    {
        var sink = new InMemoryFileSink();
        await sink.WriteAsync(File("a/b.txt"));

        var act = async () => await sink.WriteAsync(File("a/b.txt", "different"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*generated twice*");
    }

    /// <summary>Files come back ordered by path, whatever order they went in.</summary>
    [Fact]
    public async Task FilesAreOrderedByPath()
    {
        var sink = new InMemoryFileSink();

        await sink.WriteAsync(File("z.txt"));
        await sink.WriteAsync(File("a.txt"));
        await sink.WriteAsync(File("m/n.txt"));

        sink.Files.Select(file => file.Path).Should().Equal("a.txt", "m/n.txt", "z.txt");
    }

    /// <summary>A path that escapes the output root is refused.</summary>
    /// <remarks>
    /// ⚠️ Relative paths are influenced by locale codes and, from Sprint 10, by
    /// plugin ids. Refusing traversal here turns a possible arbitrary write into
    /// a generation error.
    /// </remarks>
    [Fact]
    public async Task DirectorySinkRefusesToEscapeItsRoot()
    {
        var root = Directory.CreateTempSubdirectory("shellwright-sink-");

        try
        {
            var sink = new DirectoryFileSink(root.FullName);
            var act = async () => await sink.WriteAsync(File("../escaped.txt"));

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*escapes the output directory*");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>The wrapper is written executable and everything else is not.</summary>
    [Fact]
    public async Task DirectorySinkSetsPermissionsExplicitly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("shellwright-sink-");

        try
        {
            var sink = new DirectoryFileSink(root.FullName);

            await sink.WriteAsync(new GeneratedFile(
                "gradlew", [.. Encoding.UTF8.GetBytes("#!/bin/sh")], FilePermissions.Executable));
            await sink.WriteAsync(File("build.gradle.kts"));

            var wrapper = System.IO.File.GetUnixFileMode(Path.Combine(root.FullName, "gradlew"));
            var script = System.IO.File.GetUnixFileMode(Path.Combine(root.FullName, "build.gradle.kts"));

            // A developer's umask is not an input the build cache knows about,
            // so the mode is set rather than inherited.
            wrapper.Should().HaveFlag(UnixFileMode.UserExecute);
            script.Should().NotHaveFlag(UnixFileMode.UserExecute);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    /// <summary>Reading a file that was never generated says so clearly.</summary>
    [Fact]
    public void MissingFileIsNamedInTheError()
    {
        var act = () => new InMemoryFileSink().Text("nope.txt");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*nope.txt*");
    }
}
