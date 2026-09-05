using System.Text;
using FluentAssertions;
using Shellwright.Codegen;
using Shellwright.Codegen.Normalisation;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>
/// The rules that keep output byte-identical across machines.
/// </summary>
/// <remarks>
/// ⚠️ None of these is visible in review, which is exactly why each needs a
/// test. The build cache depends on all of them.
/// </remarks>
public sealed class NormalisationTests
{
    /// <summary>Rendered output is LF, BOM-free, with exactly one final newline.</summary>
    [Theory]
    [InlineData("a\r\nb", "a\nb\n")]
    [InlineData("a\rb", "a\nb\n")]
    [InlineData("a\n\n\n", "a\n")]
    [InlineData("a", "a\n")]
    [InlineData("﻿a", "a\n")]
    public void RenderedTextIsNormalised(string input, string expected) =>
        TextNormaliser.Normalise(input).Should().Be(expected);

    /// <summary>Rendered output is composed, so two spellings hash alike.</summary>
    [Fact]
    public void RenderedTextIsComposed() =>
        TextNormaliser.Normalise("Café").Should().Be("Café\n");

    /// <summary>
    /// A copied text file's CRLF is collapsed, so the checkout cannot leak in.
    /// </summary>
    /// <remarks>
    /// This is the bug that reached CI: <c>gradlew.bat</c> sat in a working
    /// tree as CRLF while git's index held LF, <c>git status</c> called it
    /// clean, and the approved snapshot recorded a file 94 bytes larger than a
    /// fresh clone produces. The runner was right and the developer was wrong.
    /// </remarks>
    [Fact]
    public void CopiedTextHasItsLineEndingsCollapsed()
    {
        var windows = Encoding.UTF8.GetBytes("@echo off\r\nexit /b 1\r\n");

        Encoding.UTF8.GetString(TextNormaliser.NormaliseCopiedFile(windows))
            .Should().Be("@echo off\nexit /b 1\n");
    }

    /// <summary>A copied file with no trailing newline does not gain one.</summary>
    /// <remarks>
    /// Unlike rendered output. A copied file is somebody else's, and the only
    /// thing worth forcing is the property the cache depends on.
    /// </remarks>
    [Fact]
    public void CopiedTextIsNotGivenATrailingNewline()
    {
        var content = Encoding.UTF8.GetBytes("no newline at end");

        TextNormaliser.NormaliseCopiedFile(content).Should().Equal(content);
    }

    /// <summary>A lone carriage return survives; only CRLF pairs collapse.</summary>
    [Fact]
    public void LoneCarriageReturnIsLeftAlone()
    {
        var content = Encoding.UTF8.GetBytes("a\rb");

        TextNormaliser.NormaliseCopiedFile(content).Should().Equal(content);
    }

    /// <summary>Binary content is never rewritten.</summary>
    /// <remarks>
    /// ⚠️ <c>gradle-wrapper.jar</c> contains 0x0D 0x0A pairs that are not line
    /// endings. Collapsing them would produce a corrupt archive that fails at
    /// build time, in a generated project, for a reason nobody would look for
    /// in a line-ending normaliser.
    /// </remarks>
    [Fact]
    public void BinaryContentIsUntouched()
    {
        byte[] jarLike = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x0D, 0x0A, 0xFF];

        TextNormaliser.NormaliseCopiedFile(jarLike).Should().Equal(jarLike);
    }

    /// <summary>The real wrapper jar survives generation intact.</summary>
    /// <remarks>
    /// The heuristic above is only worth anything if it is right about the one
    /// binary the shell actually ships.
    /// </remarks>
    [Fact]
    public async Task WrapperJarIsCopiedByteForByte()
    {
        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.Resolve("minimal.json"), ToolchainDescriptor.Android, sink);

        var onDisk = await File.ReadAllBytesAsync(
            Path.Combine(Fixtures.AndroidShell, "gradle", "wrapper", "gradle-wrapper.jar"));

        sink.Find("gradle/wrapper/gradle-wrapper.jar")!.Content.Should().Equal(onDisk);
    }

    /// <summary>No generated text file carries a CRLF.</summary>
    /// <remarks>
    /// The blanket assertion, so a future copied file cannot reintroduce the
    /// problem without failing here first.
    /// </remarks>
    [Fact]
    public async Task NoGeneratedTextFileContainsCrLf()
    {
        var sink = new InMemoryFileSink();
        await Fixtures.Generator()
            .GenerateAsync(Fixtures.Resolve("maximal.json"), ToolchainDescriptor.Android, sink);

        foreach (var file in sink.Files)
        {
            var bytes = file.Content.AsSpan().ToArray();

            if (Array.IndexOf(bytes, (byte)0) >= 0)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(bytes);
            text.Should().NotContain("\r\n", "{0} carries Windows line endings", file.Path);
        }
    }
}
