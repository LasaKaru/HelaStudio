using System.Text.Json.Nodes;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>Shared access to the link rule list.</summary>
internal static class LinkRuleList
{
    internal static readonly HashSet<string> CatchAll =
        new(StringComparer.Ordinal) { ".*", "^.*$", ".*$", "^.*", "(.*)", ".+", "^.+$" };

    internal static List<JsonObject> Of(JsonObject config) =>
        [.. Arr(config["linkRules"]).Select(Obj)];

    internal static string? PatternOf(JsonObject rule) => Str(rule["pattern"]);

    internal static bool IsCatchAll(string pattern) => CatchAll.Contains(pattern.Trim());

    /// <summary>True when a pattern has no metacharacters beyond a leading anchor and escaped dots.</summary>
    internal static bool IsLiteralPrefix(string pattern)
    {
        var body = pattern.StartsWith('^') ? pattern[1..] : pattern;
        return !System.Text.RegularExpressions.Regex.IsMatch(body, @"(?<!\\)[.*+?[\]{}()|$]");
    }
}

/// <summary>Every user pattern must compile, and must not be able to hang the shell.</summary>
public sealed class RegexSafetyRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "regex-safety";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var (path, pattern) in AllPatterns(context.Config))
        {
            var verdict = RegexSafety.Check(pattern);
            if (verdict.Kind == RegexVerdictKind.Invalid)
            {
                yield return Diagnostic.Create(
                    DiagnosticCode.RegexInvalid,
                    Severity.Error,
                    path,
                    "This pattern is not a valid regular expression, so no link could ever match it. " +
                    "Check for an unclosed bracket or parenthesis, and remember to escape dots in domain " +
                    @"names - for example ^https://app\.acme\.com.");
            }
            else if (verdict.Kind == RegexVerdictKind.Catastrophic)
            {
                yield return Diagnostic.Create(
                    DiagnosticCode.RegexCatastrophic,
                    Severity.Error,
                    path,
                    $"The construct {verdict.Detail} nests one repetition inside another, which can take " +
                    "exponential time to match and would freeze the app on every navigation. " +
                    "Rewrite it with a single repetition, for example (a+) instead of (a+)+.");
            }
        }
    }

    private static IEnumerable<(string Path, string Pattern)> AllPatterns(JsonObject config)
    {
        var rules = LinkRuleList.Of(config);
        for (var i = 0; i < rules.Count; i++)
        {
            if (LinkRuleList.PatternOf(rules[i]) is { } pattern)
            {
                yield return (JsonPointer.Of("linkRules", i, "pattern"), pattern);
            }
        }

        var items = Arr(Obj(Obj(config["navigation"])["tabBar"])["items"]);
        for (var i = 0; i < items.Count; i++)
        {
            if (Str(Obj(items[i])["activePattern"]) is { } activePattern)
            {
                yield return (JsonPointer.Of("navigation", "tabBar", "items", i, "activePattern"), activePattern);
            }
        }
    }
}

/// <summary>A rule shadowed by an earlier, broader rule can never fire.</summary>
public sealed class UnreachableLinkRuleRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "link-rule-unreachable";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rules = LinkRuleList.Of(context.Config);
        for (var i = 1; i < rules.Count; i++)
        {
            if (LinkRuleList.PatternOf(rules[i]) is null)
            {
                continue;
            }

            if (FindShadower(rules, i) is not { } shadower)
            {
                continue;
            }

            yield return Diagnostic.Create(
                DiagnosticCode.LinkRuleUnreachable,
                Severity.Warning,
                JsonPointer.Of("linkRules", i, "pattern"),
                $"Rule {i + 1} can never match, because rule {shadower + 1} above it already " +
                "matches everything it would. Move this rule above that one, or remove it.");
        }
    }

    private static int? FindShadower(List<JsonObject> rules, int index)
    {
        if (LinkRuleList.PatternOf(rules[index]) is not { } pattern)
        {
            return null;
        }

        for (var j = 0; j < index; j++)
        {
            if (LinkRuleList.PatternOf(rules[j]) is not { } earlier)
            {
                continue;
            }

            if (LinkRuleList.IsCatchAll(earlier))
            {
                return j;
            }

            // A literal prefix that is a prefix of this one means this one is subsumed.
            if (earlier != pattern
                && pattern.StartsWith(earlier, StringComparison.Ordinal)
                && LinkRuleList.IsLiteralPrefix(earlier))
            {
                return j;
            }
        }

        return null;
    }
}

/// <summary>Without a terminal catch-all, unmatched links have undefined behaviour.</summary>
public sealed class CatchAllLinkRuleRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "link-rule-catchall";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rules = LinkRuleList.Of(context.Config);
        if (rules.Count == 0)
        {
            yield break;
        }

        if (LinkRuleList.PatternOf(rules[^1]) is { } last && LinkRuleList.IsCatchAll(last))
        {
            yield break;
        }

        yield return Diagnostic.Create(
            DiagnosticCode.LinkRuleNoCatchall,
            Severity.Warning,
            JsonPointer.Of("linkRules"),
            "No rule matches every remaining link, so it is not defined where an unrecognised link opens. " +
            "Add a final rule with the pattern \".*\" and the action you want as the fallback, " +
            "usually externalBrowser.");
    }
}
