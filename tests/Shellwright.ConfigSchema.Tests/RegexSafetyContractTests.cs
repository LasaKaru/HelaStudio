using System.Text.Json;
using FluentAssertions;
using Shellwright.ConfigSchema.Rules;
using Xunit;

namespace Shellwright.ConfigSchema.Tests;

/// <summary>
/// The backtracking-heuristic contract, shared with the TypeScript validator and
/// both shells.
/// </summary>
/// <remarks>
/// <para>
/// A user's link-rule pattern is checked in the studio (TypeScript), here, and
/// again by each shell before it is run on a phone (Kotlin, Swift). Four
/// implementations of one judgement, sharing no code.
/// </para>
/// <para>
/// Disagreement is not academic. If the studio accepts a pattern a shell then
/// refuses, a customer's rule silently stops working. If the studio accepts one a
/// shell <em>runs</em>, the app freezes on every navigation — and on iOS nothing
/// can interrupt it. <c>tests/fixtures/regex-safety/README.md</c> records the two
/// cases deliberately left out because the engines genuinely differ.
/// </para>
/// </remarks>
public sealed class RegexSafetyContractTests
{
    public static TheoryData<string, string, string> Corpus()
    {
        var path = Path.Combine(Fixtures.RegexSafetyDir, "patterns.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var data = new TheoryData<string, string, string>();
        foreach (var element in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            data.Add(
                element.GetProperty("pattern").GetString()!,
                element.GetProperty("verdict").GetString()!,
                element.GetProperty("why").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ClassifiesEveryPatternAsDeclared(string pattern, string verdict, string why)
    {
        var expected = verdict switch
        {
            "ok" => RegexVerdictKind.Ok,
            "invalid" => RegexVerdictKind.Invalid,
            "catastrophic" => RegexVerdictKind.Catastrophic,
            _ => throw new InvalidOperationException($"Unknown verdict '{verdict}' in the corpus."),
        };

        RegexSafety.Check(pattern).Kind.Should().Be(expected, why);
    }

    [Fact]
    public void CorpusCoversEveryVerdict()
    {
        // A verdict with no case in the corpus is one the other three
        // implementations are not held to at all.
        var covered = Corpus()
            .Select(row => (string)row[1]!)
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal);

        covered.Should().Equal("catastrophic", "invalid", "ok");
    }
}
