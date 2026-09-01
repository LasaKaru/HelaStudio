using FluentAssertions;
using Shellwright.Codegen.Templating;
using Xunit;

namespace Shellwright.Codegen.Tests;

/// <summary>
/// The escapers, character by character.
/// </summary>
/// <remarks>
/// These are unit tests rather than golden tests on purpose. A golden file
/// shows that output changed; it does not say which rule was meant to apply. If
/// one of these ever fails, the message should name the rule.
/// </remarks>
public sealed class EscaperTests
{
    /// <summary>Text bound for an Android string resource. TC-S04-GEN-010.</summary>
    /// <param name="input">The raw value.</param>
    /// <param name="expected">The escaped value.</param>
    /// <remarks>
    /// The apostrophe is the whole reason this class exists: it is legal in an
    /// app name, common in real ones, and makes aapt2 fail with an error that
    /// mentions neither apostrophes nor the string it came from.
    /// </remarks>
    [Theory]
    [InlineData("Bob's Diner", @"Bob\'s Diner")]
    [InlineData("Say \"hi\"", "Say \\\"hi\\\"")]
    [InlineData("Fish & Chips", "Fish &amp; Chips")]
    [InlineData("a < b", "a &lt; b")]
    [InlineData("a > b", "a &gt; b")]
    [InlineData(@"back\slash", @"back\\slash")]
    // A leading @ or ? makes the resource compiler read the value as a
    // reference to another resource rather than as text.
    [InlineData("@home", @"\@home")]
    [InlineData("?query", @"\?query")]
    // A $ is not special to XML or to aapt2 — only to Kotlin.
    [InlineData("$5 menu", "$5 menu")]
    // Scripts pass through untouched. Escaping is about punctuation.
    [InlineData("متجر أكمي", "متجر أكمي")]
    [InlineData("東京", "東京")]
    public void AndroidResource(string input, string expected) =>
        Escapers.Escape(input, TemplateFormat.AndroidResource).Should().Be(expected);

    /// <summary>Leading and trailing spaces survive only inside quotes.</summary>
    [Fact]
    public void AndroidResourceQuotesSurroundingWhitespace()
    {
        // aapt2 strips unquoted leading and trailing whitespace, which would
        // silently change a customer's app name rather than fail.
        Escapers.Escape("  Padded  ", TemplateFormat.AndroidResource)
            .Should().Be("\"  Padded  \"");
    }

    /// <summary>A Kotlin DSL string literal. TC-S04-GEN-011.</summary>
    /// <param name="input">The raw value.</param>
    /// <param name="expected">The escaped value.</param>
    /// <remarks>
    /// <c>$</c> opens a template expression in Kotlin, so an unescaped one
    /// either fails the build or, worse, interpolates a variable that happens
    /// to exist.
    /// </remarks>
    [Theory]
    [InlineData("$5 menu", @"\$5 menu")]
    [InlineData("Say \"hi\"", "Say \\\"hi\\\"")]
    [InlineData(@"back\slash", @"back\\slash")]
    [InlineData("Bob's Diner", "Bob's Diner")]
    [InlineData("a & b < c", "a & b < c")]
    public void GradleKotlin(string input, string expected) =>
        Escapers.Escape(input, TemplateFormat.GradleKotlin).Should().Be(expected);

    /// <summary>XML attributes need entities, not backslashes.</summary>
    /// <param name="input">The raw value.</param>
    /// <param name="expected">The escaped value.</param>
    [Theory]
    [InlineData("Bob's", "Bob&apos;s")]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("\"quoted\"", "&quot;quoted&quot;")]
    public void Xml(string input, string expected) =>
        Escapers.Escape(input, TemplateFormat.Xml).Should().Be(expected);

    /// <summary>JSON escaping matches the Sprint 01 canonicaliser exactly.</summary>
    /// <remarks>
    /// Two spellings of one rule is one too many when a cache key depends on
    /// it: a string embedded in a generated file must escape exactly as the
    /// same string does in the hashed config.
    /// </remarks>
    [Fact]
    public void JsonMatchesTheCanonicaliser()
    {
        const string Value = "line\nbreak \"quoted\" back\\slash";

        var canonical = ConfigSchema.CanonicalJson.EscapeString(Value);
        Escapers.Escape(Value, TemplateFormat.Json).Should().Be(canonical[1..^1]);
    }

    /// <summary>Every format normalises to NFC before escaping.</summary>
    /// <remarks>
    /// ⚠️ Two configs that differ only in Unicode composition look identical,
    /// hash differently, and would otherwise generate byte-different projects —
    /// a cache miss for a difference nobody can see. This is the same trap the
    /// C# canonicaliser fell into in Sprint 02, when
    /// <c>InvariantGlobalization</c> made <c>Normalize</c> a silent no-op.
    /// </remarks>
    /// <param name="format">The format under test.</param>
    [Theory]
    [InlineData(TemplateFormat.AndroidResource)]
    [InlineData(TemplateFormat.Xml)]
    [InlineData(TemplateFormat.GradleKotlin)]
    [InlineData(TemplateFormat.Json)]
    [InlineData(TemplateFormat.None)]
    public void NormalisesToComposedForm(TemplateFormat format)
    {
        const string Decomposed = "Café";
        const string Composed = "Café";

        Escapers.Escape(Decomposed, format)
            .Should().Be(Escapers.Escape(Composed, format));
    }
}
