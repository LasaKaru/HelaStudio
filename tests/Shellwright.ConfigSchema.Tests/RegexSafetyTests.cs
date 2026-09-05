using FluentAssertions;
using Shellwright.ConfigSchema.Rules;
using Xunit;

namespace Shellwright.ConfigSchema.Tests;

/// <summary>
/// The backtracking checker, tested against the same table as the TypeScript side.
/// </summary>
/// <remarks>
/// The two implementations use different regex engines, so agreement on this
/// table is not automatic. It is the only reason a pattern accepted in the
/// browser is also accepted on the build runner.
/// </remarks>
public sealed class RegexSafetyTests
{
    [Theory]
    // Nested repetition: the shapes that explode.
    [InlineData("^(a+)+$", RegexVerdictKind.Catastrophic)]
    [InlineData("^(a*)*$", RegexVerdictKind.Catastrophic)]
    [InlineData("(a|a)*$", RegexVerdictKind.Catastrophic)]
    [InlineData("^(x+x+)+y$", RegexVerdictKind.Catastrophic)]
    [InlineData("(?:a+)+", RegexVerdictKind.Catastrophic)]
    [InlineData("(?<name>a+)+", RegexVerdictKind.Catastrophic)]
    [InlineData("([a-z]+)+", RegexVerdictKind.Catastrophic)]
    [InlineData(@"(\d+)+", RegexVerdictKind.Catastrophic)]
    [InlineData("(a|ab)*", RegexVerdictKind.Catastrophic)]
    [InlineData("(a{2,})+", RegexVerdictKind.Catastrophic)]
    // Safe: the separated-list idiom and other ordinary patterns.
    [InlineData("^[a-z]+(-[a-z]+)*$", RegexVerdictKind.Ok)]
    [InlineData(@"^https://app\.acme\.com", RegexVerdictKind.Ok)]
    [InlineData(".*", RegexVerdictKind.Ok)]
    [InlineData("^/orders/[0-9]+$", RegexVerdictKind.Ok)]
    [InlineData("(abc)+", RegexVerdictKind.Ok)]
    [InlineData("(a+)?", RegexVerdictKind.Ok)]
    [InlineData("(a+)+?", RegexVerdictKind.Ok)]
    [InlineData("(a{2})+", RegexVerdictKind.Ok)]
    [InlineData("(a{2,4})+", RegexVerdictKind.Ok)]
    [InlineData("(-a|-b)*", RegexVerdictKind.Ok)]
    [InlineData("(cat|dog|bird)*", RegexVerdictKind.Ok)]
    [InlineData("(x[a|b]y)*", RegexVerdictKind.Ok)]
    [InlineData(@"\(a+\)+", RegexVerdictKind.Ok)]
    [InlineData("(^a)+", RegexVerdictKind.Ok)]
    [InlineData("([a-z]x)+", RegexVerdictKind.Ok)]
    [InlineData(@"(\d)+", RegexVerdictKind.Ok)]
    [InlineData("", RegexVerdictKind.Ok)]
    // Uncompilable.
    [InlineData("(", RegexVerdictKind.Invalid)]
    [InlineData("[z-a]", RegexVerdictKind.Invalid)]
    [InlineData("(a+", RegexVerdictKind.Invalid)]
    public void Classifies_patterns(string pattern, RegexVerdictKind expected)
    {
        RegexSafety.Check(pattern).Kind.Should().Be(expected);
    }

    [Fact]
    public void Rejects_a_pathological_pattern_without_hanging()
    {
        var pattern = $"^({string.Concat(Enumerable.Repeat("a+", 50))})+$";

        var started = System.Diagnostics.Stopwatch.StartNew();
        var verdict = RegexSafety.Check(pattern);
        started.Stop();

        verdict.Kind.Should().Be(RegexVerdictKind.Catastrophic);
        started.ElapsedMilliseconds.Should().BeLessThan(50);
    }
}
