using System.Text;
using Shellwright.ConfigSchema;

namespace Shellwright.Codegen.Templating;

/// <summary>The output format a value is being written into.</summary>
/// <remarks>
/// Chosen per template file, not per value. See <see cref="TemplateModel"/> for
/// why the format is a property of the whole model rather than something a
/// template author remembers to apply.
/// </remarks>
public enum TemplateFormat
{
    /// <summary>Text inside an Android <c>&lt;string&gt;</c> resource.</summary>
    AndroidResource,

    /// <summary>An XML attribute value or element text, with no Android rules.</summary>
    Xml,

    /// <summary>A string literal in a Gradle Kotlin DSL script.</summary>
    GradleKotlin,

    /// <summary>A JSON string, without the surrounding quotes.</summary>
    Json,

    /// <summary>No escaping. Only for values that are not attacker-influenced.</summary>
    None,
}

/// <summary>
/// Escapes a value for one output format.
/// </summary>
/// <remarks>
/// <para>
/// Every value written into a generated project passes through here. Getting
/// this wrong does not produce a crash — it produces a project that fails to
/// compile for one customer, in a way that reads as a platform bug.
/// </para>
/// <para>
/// Every string here is NFC-normalised first, matching
/// <see cref="CanonicalJson"/>. Two configs that differ only by Unicode
/// composition must produce byte-identical projects, or the build cache misses
/// for a difference nobody can see.
/// </para>
/// </remarks>
public static class Escapers
{
    /// <summary>Escapes <paramref name="value"/> for <paramref name="format"/>.</summary>
    /// <param name="value">The raw value.</param>
    /// <param name="format">The target format.</param>
    /// <returns>The escaped value, safe to interpolate.</returns>
    public static string Escape(string value, TemplateFormat format)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Normalize(NormalizationForm.FormC);

        return format switch
        {
            TemplateFormat.AndroidResource => AndroidResource(normalized),
            TemplateFormat.Xml => Xml(normalized),
            TemplateFormat.GradleKotlin => GradleKotlin(normalized),
            TemplateFormat.Json => Json(normalized),
            TemplateFormat.None => normalized,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>
    /// Escapes text destined for an Android <c>&lt;string&gt;</c> resource.
    /// </summary>
    /// <remarks>
    /// ⚠️ Two escaping systems apply at once and they are frequently confused.
    /// XML entities handle <c>&amp;</c> and <c>&lt;</c>; on top of that, the
    /// Android resource compiler applies its own rules to the *decoded* text —
    /// an apostrophe must be <c>\'</c> or `aapt2` fails with a message that
    /// says nothing about apostrophes. That single character is the most common
    /// generated-project build failure in this whole class of product, because
    /// it only appears for customers whose app name contains one.
    /// </remarks>
    private static string AndroidResource(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var ch in value)
        {
            switch (ch)
            {
                // Backslash first: everything below adds backslashes, and
                // escaping them again afterwards would double them.
                case '\\': builder.Append(@"\\"); break;
                case '\'': builder.Append(@"\'"); break;
                case '"': builder.Append("\\\""); break;
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '\n': builder.Append(@"\n"); break;
                case '\t': builder.Append(@"\t"); break;
                case '\r': break;
                default: builder.Append(ch); break;
            }
        }

        var escaped = builder.ToString();

        // A leading @ or ? makes the resource compiler read the value as a
        // reference to another resource rather than as text.
        if (escaped.StartsWith('@') || escaped.StartsWith('?'))
        {
            escaped = @"\" + escaped;
        }

        // Leading and trailing whitespace is stripped unless the value is
        // quoted, which silently changes a customer's app name.
        if (escaped.Length > 0 && (char.IsWhiteSpace(escaped[0]) || char.IsWhiteSpace(escaped[^1])))
        {
            escaped = "\"" + escaped + "\"";
        }

        return escaped;
    }

    /// <summary>Escapes a value for an XML attribute or element body.</summary>
    private static string Xml(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&apos;"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Escapes a value for a Kotlin DSL string literal.</summary>
    /// <remarks>
    /// ⚠️ <c>$</c> is the one people forget. In Kotlin it opens a template
    /// expression, so an unescaped <c>$</c> in an app name turns a build script
    /// into a compile error — or, worse, silently interpolates a variable that
    /// happens to exist.
    /// </remarks>
    private static string GradleKotlin(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append(@"\\"); break;
                case '"': builder.Append("\\\""); break;
                case '$': builder.Append(@"\$"); break;
                case '\n': builder.Append(@"\n"); break;
                case '\r': builder.Append(@"\r"); break;
                case '\t': builder.Append(@"\t"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Escapes a value for a JSON string, without the quotes.</summary>
    /// <remarks>
    /// Delegates to the Sprint 01 canonicaliser and strips its quotes, so a
    /// string embedded in a generated file escapes exactly as the same string
    /// does in a hashed config. Two spellings of one rule is one spelling too
    /// many when a cache key depends on it.
    /// </remarks>
    private static string Json(string value)
    {
        var quoted = CanonicalJson.EscapeString(value);
        return quoted[1..^1];
    }
}
