using System.Globalization;
using System.Text;

namespace Shellwright.Api.Data;

/// <summary>Turns a display name into a URL-safe identifier.</summary>
public static class Slug
{
    /// <summary>Maximum length, matching the column.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Derives a slug from arbitrary text.
    /// </summary>
    /// <param name="text">The text to convert.</param>
    /// <returns>A lowercase ASCII slug, possibly empty.</returns>
    /// <remarks>
    /// ⚠️ Returns an empty string rather than a placeholder when nothing
    /// survives — a name written entirely in a script this reduction cannot
    /// handle is a case for the caller to reject with a message, not for this
    /// function to paper over by inventing "untitled".
    ///
    /// The reduction strips diacritics first so that "Café" and "Cafe" collide
    /// rather than producing two organisations whose URLs differ by a character
    /// most people cannot type.
    /// </remarks>
    public static string From(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSeparator = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingSeparator = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSeparator = true;
            }
        }

        var slug = builder.ToString();
        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }
}
