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
