using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Shellwright.Api.Endpoints;

/// <summary>
/// Opaque cursors for keyset pagination.
/// </summary>
/// <remarks>
/// ⚠️ Keyset rather than offset, and not for performance. <c>OFFSET 40</c> over
/// a list somebody is actively appending to silently skips and repeats rows —
/// a customer paging through their version history while autosave is running
/// would see versions twice and miss others, with nothing to indicate it. A
/// cursor anchored to the last row read cannot do that.
///
/// The encoding is base64url of "ticks:guid" — opaque to clients, so the
/// ordering can change without breaking anyone holding one, but not encrypted:
/// it carries nothing the caller could not already see in the row it came from.
/// </remarks>
public static class Cursor
{
    /// <summary>Encodes the position after a row.</summary>
    /// <param name="createdAt">The row's timestamp.</param>
    /// <param name="id">The row's identifier, breaking ties.</param>
    /// <returns>An opaque cursor.</returns>
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{createdAt.UtcTicks}:{id}");

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>Decodes a cursor.</summary>
    /// <param name="cursor">The value the client sent.</param>
    /// <param name="position">The decoded position.</param>
    /// <returns>True when the cursor was well formed.</returns>
    public static bool TryDecode(string? cursor, out (DateTimeOffset CreatedAt, Guid Id) position)
    {
        position = default;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = raw.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        if (!long.TryParse(raw[..separator], CultureInfo.InvariantCulture, out var ticks)
            || !Guid.TryParse(raw[(separator + 1)..], out var id)
            || ticks < 0
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        position = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
