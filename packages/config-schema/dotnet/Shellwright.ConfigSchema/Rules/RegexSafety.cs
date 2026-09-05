namespace Shellwright.ConfigSchema.Rules;

/// <summary>Why a pattern was accepted or rejected.</summary>
public enum RegexVerdictKind
{
    /// <summary>The pattern compiles and matches in linear time.</summary>
    Ok,

    /// <summary>The pattern does not compile.</summary>
    Invalid,

    /// <summary>The pattern can backtrack catastrophically.</summary>
    Catastrophic,
}

/// <summary>The outcome of checking one user-supplied pattern.</summary>
/// <param name="Kind">Whether the pattern is usable.</param>
/// <param name="Detail">The compiler message, or the offending construct.</param>
public sealed record RegexVerdict(RegexVerdictKind Kind, string Detail = "");

/// <summary>
/// Detects regular expressions that are invalid or vulnerable to catastrophic
/// backtracking.
/// </summary>
/// <remarks>
/// This matters more here than in most codebases: user patterns are evaluated by
/// the shell on every navigation, on a phone. A pattern like <c>^(a+)+$</c> does
/// not merely slow a server down, it freezes the customer's app.
///
/// The analysis must agree with <c>src/rules/regex-safety.ts</c>.
/// </remarks>
public static class RegexSafety
{
    private const string Quantifiers = "*+?{";

    /// <summary>Checks a pattern for compilability and backtracking safety.</summary>
    /// <param name="pattern">The user-supplied pattern.</param>
    /// <returns>The verdict.</returns>
    public static RegexVerdict Check(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern);
        }
        catch (ArgumentException error)
        {
            return new RegexVerdict(RegexVerdictKind.Invalid, error.Message);
        }

        var construct = FindNestedQuantifier(pattern);
        return construct is null
            ? new RegexVerdict(RegexVerdictKind.Ok)
            : new RegexVerdict(RegexVerdictKind.Catastrophic, construct);
    }

    /// <summary>
    /// Finds a quantified group whose body is itself quantified or alternated —
    /// the <c>(a+)+</c> and <c>(a|a)*</c> shapes that cause exponential backtracking.
    /// </summary>
    private static string? FindNestedQuantifier(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] != '(' || IsEscaped(pattern, i))
            {
                continue;
            }

            var close = MatchingParen(pattern, i);
            if (close is not { } end || end + 1 >= pattern.Length)
            {
                continue;
            }

            var after = pattern[end + 1];
            if (!Quantifiers.Contains(after, StringComparison.Ordinal))
            {
                continue;
            }

            // `(...)?` cannot blow up: the group is tried at most once.
            if (after == '?')
            {
                continue;
            }

            // A lazy or possessive quantifier bounds the search.
            if (end + 2 < pattern.Length && (pattern[end + 2] == '?' || pattern[end + 2] == '+'))
            {
                continue;
            }

            if (BodyIsAmbiguous(pattern[(i + 1)..end]))
            {
                return pattern[i..(end + 2)];
            }
        }

        return null;
    }

    /// <summary>
    /// True when a group body can match the same text more than one way.
    /// </summary>
    /// <remarks>
    /// Note what is deliberately not flagged: <c>(-[a-z]+)*</c>, the ordinary
    /// separated-list idiom. Its body must consume a literal dash before anything
    /// else, so repetitions cannot overlap and matching stays linear.
    /// </remarks>
    private static bool BodyIsAmbiguous(string body)
    {
        var inner = StripGroupPrefix(body);
        return StartsWithQuantifiedAtom(inner) || HasOverlappingAlternation(inner);
    }

    private static string StripGroupPrefix(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body,
            @"^\?(?::|=|!|<[=!]|<[A-Za-z_$][\w$]*>)",
            string.Empty);

    private static bool StartsWithQuantifiedAtom(string body)
    {
        if (FirstAtomEnd(body) is not { } end || end >= body.Length)
        {
            return false;
        }

        var quantifier = body[end];
        if (quantifier is '*' or '+')
        {
            return true;
        }

        // `{n,}` is unbounded; `{n}` and `{n,m}` are not, so they cannot explode.
        if (quantifier != '{')
        {
            return false;
        }

        var close = body.IndexOf('}', end);
        return close != -1 && body[(end + 1)..close].EndsWith(',');
    }

    private static int? FirstAtomEnd(string body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        var first = body[0];
        if (first == '\\')
        {
            return body.Length > 1 ? 2 : null;
        }

        if (first == '[')
        {
            return ClassEnd(body) is { } close ? close + 1 : null;
        }

        if (first == '(')
        {
            return MatchingParen(body, 0) is { } close ? close + 1 : null;
        }

        // A quantifier cannot open an atom, and an anchor consumes nothing.
        return "*+?{|)^$".Contains(first, StringComparison.Ordinal) ? null : 1;
    }

    private static int? ClassEnd(string body)
    {
        for (var i = 1; i < body.Length; i++)
        {
            if (!IsEscaped(body, i) && body[i] == ']')
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// True when two top-level branches can match the same text.
    /// </summary>
    /// <remarks>
    /// Only identical branches, or one branch that is a prefix of another, are
    /// genuinely ambiguous. <c>(-a|-b)</c> merely shares a first character and
    /// stays linear, so it is left alone.
    /// </remarks>
    private static bool HasOverlappingAlternation(string body)
    {
        var branches = SplitTopLevel(body);
        if (branches.Count < 2)
        {
            return false;
        }

        for (var i = 0; i < branches.Count; i++)
        {
            for (var j = i + 1; j < branches.Count; j++)
            {
                var a = branches[i];
                var b = branches[j];
                if (a == b || a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string> SplitTopLevel(string body)
    {
        var branches = new List<string>();
        var depth = 0;
        var inClass = false;
        var start = 0;

        for (var i = 0; i < body.Length; i++)
        {
            if (IsEscaped(body, i))
            {
                continue;
            }

            var ch = body[i];
            if (inClass)
            {
                if (ch == ']')
                {
                    inClass = false;
                }

                continue;
            }

            switch (ch)
            {
                case '[':
                    inClass = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case '|' when depth == 0:
                    branches.Add(body[start..i]);
                    start = i + 1;
                    break;
                default:
                    break;
            }
        }

        branches.Add(body[start..]);
        return branches;
    }

    private static int? MatchingParen(string pattern, int open)
    {
        var depth = 0;
        var inClass = false;

        for (var i = open; i < pattern.Length; i++)
        {
            if (IsEscaped(pattern, i))
            {
                continue;
            }

            var ch = pattern[i];
            if (inClass)
            {
                if (ch == ']')
                {
                    inClass = false;
                }

                continue;
            }

            switch (ch)
            {
                case '[':
                    inClass = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')' when --depth == 0:
                    return i;
                default:
                    break;
            }
        }

        return null;
    }

    private static bool IsEscaped(string text, int index)
    {
        var backslashes = 0;
        for (var i = index - 1; i >= 0 && text[i] == '\\'; i--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }
}
