using System.Text;

namespace Shellwright.Codegen.Normalisation;

/// <summary>
/// Puts generated text into the one form the whole system agrees on.
/// </summary>
/// <remarks>
/// ⚠️ Each rule here is a source of nondeterminism that would otherwise reach
/// the cache key. None of them is visible in a diff, which is precisely why
/// they have to be enforced mechanically rather than by convention:
/// <list type="bullet">
///   <item>CRLF, from a template edited on Windows.</item>
///   <item>A byte-order mark, from an editor that adds one silently.</item>
///   <item>A missing or doubled final newline, from a trailing blank line.</item>
///   <item>Decomposed Unicode, which hashes differently but looks identical.</item>
/// </list>
/// </remarks>
public static class TextNormaliser
{
    /// <summary>UTF-8 without a byte-order mark.</summary>
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const char ByteOrderMark = '﻿';

    /// <summary>Normalises text and encodes it.</summary>
    /// <param name="text">The rendered text.</param>
    /// <returns>UTF-8 bytes, LF line endings, exactly one final newline.</returns>
    public static byte[] ToBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Utf8NoBom.GetBytes(Normalise(text));
    }

    /// <summary>
    /// Normalises line endings in a file that is copied rather than rendered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Copied files are just as capable of breaking byte-identity as
    /// rendered ones, and they are easier to forget because nothing in the
    /// generator touches them. A developer whose working tree holds
    /// <c>gradlew.bat</c> with CRLF — which happens on Windows, and happened
    /// here from a stale checkout that <c>git status</c> reported clean —
    /// produces a project 94 bytes different from a fresh clone's. The
    /// snapshot then encodes one machine's checkout settings, and CI is right
    /// and the developer is wrong.
    /// </para>
    /// <para>
    /// Binary content is returned untouched: rewriting bytes inside
    /// <c>gradle-wrapper.jar</c> because they happen to be 0x0D 0x0A would
    /// corrupt it. A NUL byte is the same text/binary heuristic git uses, and
    /// it is right about every file in the shell.
    /// </para>
    /// <para>
    /// No trailing newline is imposed here, unlike <see cref="Normalise"/>.
    /// A copied file is somebody else's, and the only thing worth forcing is
    /// the property the build cache depends on.
    /// </para>
    /// </remarks>
    /// <param name="content">The file's bytes as read from disk.</param>
    /// <returns>The same bytes with CRLF collapsed to LF, or unchanged if binary.</returns>
    public static byte[] NormaliseCopiedFile(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (Array.IndexOf(content, (byte)0) >= 0)
        {
            return content;
        }

        var carriageReturn = Array.IndexOf(content, (byte)'\r');

        if (carriageReturn < 0)
        {
            return content;
        }

        var result = new List<byte>(content.Length);

        for (var i = 0; i < content.Length; i++)
        {
            // A lone CR is left alone: collapsing it would change the file's
            // meaning rather than its encoding.
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                continue;
            }

            result.Add(content[i]);
        }

        return [.. result];
    }

    /// <summary>Normalises text without encoding it.</summary>
    /// <param name="text">The rendered text.</param>
    /// <returns>The normalised text.</returns>
    public static string Normalise(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalised = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .TrimStart(ByteOrderMark)
            .Normalize(NormalizationForm.FormC);

        return normalised.TrimEnd('\n') + "\n";
    }
}
