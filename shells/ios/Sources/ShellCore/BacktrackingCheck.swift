import Foundation

/// Whether a user-supplied pattern can be run on the main thread safely.
public enum RegexVerdict: Equatable, Sendable {
    /// Compiles, and shows none of the shapes that backtrack exponentially.
    case ok
    /// Does not compile.
    case invalid
    /// Compiles, but contains `construct` — a quantified group whose body is
    /// itself ambiguous, as in `(a+)+`.
    case catastrophic(construct: String)
}

/// Detects patterns that would freeze the app on every navigation.
///
/// This is a port of the studio's `checkRegex` (`packages/config-schema/src/
/// rules/regex-safety.ts`), and it must agree with it, with the C# validator,
/// and with the Kotlin shell for every input. The shared corpus in
/// `tests/fixtures/regex-safety/` is what holds the four together.
///
/// It is a deliberately conservative structural scan rather than a full parse.
/// It catches the shapes seen in practice without needing to model an engine,
/// and — just as importantly — it leaves alone the shapes that look dangerous
/// but are not.
///
/// - Important: On iOS this check is not defence in depth, it *is* the defence.
///   The Android shell can interrupt a runaway match by counting the reads
///   `java.util.regex` makes against the `CharSequence` it was handed.
///   `NSRegularExpression` is ICU-backed and does not yield while it
///   backtracks: the block passed to `enumerateMatches(in:options:range:using:)`
///   is called for matches, not for progress, so there is no point at which a
///   deadline could be observed. A pattern that gets past this check and then
///   explodes will hang the process until the watchdog kills it.
public enum BacktrackingCheck {

    /// Classifies `pattern`.
    public static func verdict(for pattern: String) -> RegexVerdict {
        guard (try? NSRegularExpression(pattern: pattern)) != nil else { return .invalid }

        guard let construct = findNestedQuantifier(Array(pattern)) else { return .ok }
        return .catastrophic(construct: construct)
    }

    /// Quantifiers that repeat a group. `?` is excluded: it tries the group at
    /// most once and so cannot compound.
    private static let quantifiers: Set<Character> = ["*", "+", "{"]

    /// Finds a quantified group whose body is itself quantified or alternated —
    /// the `(a+)+` and `(a|a)*` shapes that cause exponential backtracking.
    ///
    /// Returns the offending substring, or nil when the pattern looks safe.
    private static func findNestedQuantifier(_ p: [Character]) -> String? {
        for i in p.indices {
            guard p[i] == "(", !isEscaped(p, i) else { continue }
            guard let close = matchingParen(p, from: i) else { continue }

            let afterIndex = close + 1
            guard afterIndex < p.count else { continue }
            let after = p[afterIndex]
            guard quantifiers.contains(after) else { continue }

            // A lazy or possessive quantifier bounds the search.
            let modifier = afterIndex + 1 < p.count ? p[afterIndex + 1] : nil
            if modifier == "?" || modifier == "+" { continue }

            let body = Array(p[(i + 1)..<close])
            if bodyIsAmbiguous(body) {
                return String(p[i...afterIndex])
            }
        }
        return nil
    }

    /// True when a group body can match the same text more than one way.
    ///
    /// Two shapes cause exponential backtracking when wrapped in an outer
    /// repetition:
    ///
    ///   1. The body *begins* with a quantified atom, as in `(a+)+`. The inner
    ///      and outer repetitions then compete for the same characters.
    ///   2. The body is a top-level alternation whose branches overlap, as in
    ///      `(a|a)*` or `(a|ab)*`.
    ///
    /// Note what is deliberately *not* flagged: `(-[a-z]+)*`, the ordinary
    /// separated-list idiom. Its body must consume a literal `-` before
    /// anything else, so repetitions cannot overlap and matching stays linear.
    /// Rejecting it would be a false positive on one of the most common
    /// patterns users write.
    private static func bodyIsAmbiguous(_ body: [Character]) -> Bool {
        let inner = stripGroupPrefix(body)
        return startsWithQuantifiedAtom(inner) || hasOverlappingAlternation(inner)
    }

    /// Removes a non-capturing, lookaround, or named-group prefix such as `?:`.
    private static func stripGroupPrefix(_ body: [Character]) -> [Character] {
        guard body.first == "?" else { return body }
        let rest = Array(body.dropFirst())

        switch rest.first {
        case ":", "=", "!":
            return Array(rest.dropFirst())
        case "<":
            // `?<=` and `?<!` are lookbehind; `?<name>` is a named group.
            if rest.count > 1, rest[1] == "=" || rest[1] == "!" {
                return Array(rest.dropFirst(2))
            }
            guard let close = rest.firstIndex(of: ">") else { return body }
            return Array(rest[(close + 1)...])
        default:
            return body
        }
    }

    /// True when the first atom of `body` carries an unbounded quantifier.
    private static func startsWithQuantifiedAtom(_ body: [Character]) -> Bool {
        guard let end = firstAtomEnd(body), end < body.count else { return false }

        switch body[end] {
        case "*", "+":
            return true
        case "{":
            // `{n,}` is unbounded; `{n}` and `{n,m}` are not, so they cannot
            // explode.
            guard let close = body[end...].firstIndex(of: "}") else { return false }
            return body[(end + 1)..<close].last == ","
        default:
            return false
        }
    }

    /// Index just past the first atom in `body`, or nil if there is none.
    private static func firstAtomEnd(_ body: [Character]) -> Int? {
        guard let first = body.first else { return nil }

        switch first {
        case "\\":
            return body.count > 1 ? 2 : nil
        case "[":
            guard let close = classEnd(body) else { return nil }
            return close + 1
        case "(":
            guard let close = matchingParen(body, from: 0) else { return nil }
            return close + 1
        // A quantifier cannot open an atom, and an anchor consumes nothing.
        case "*", "+", "?", "{", "|", ")", "^", "$":
            return nil
        default:
            return 1
        }
    }

    /// End index of a character class starting at position 0, or nil.
    private static func classEnd(_ body: [Character]) -> Int? {
        for i in 1..<max(body.count, 1) where !isEscaped(body, i) {
            if body[i] == "]" { return i }
        }
        return nil
    }

    /// True when two top-level branches can match the same text.
    ///
    /// Only identical branches, or one branch that is a prefix of another, are
    /// genuinely ambiguous. `(-a|-b)` merely shares a first character and stays
    /// linear, so it is left alone.
    private static func hasOverlappingAlternation(_ body: [Character]) -> Bool {
        let branches = splitTopLevel(body)
        guard branches.count > 1 else { return false }

        for i in branches.indices {
            for j in branches.indices where j > i {
                let a = branches[i]
                let b = branches[j]
                if a.hasPrefix(b) || b.hasPrefix(a) { return true }
            }
        }
        return false
    }

    /// Splits a body on top-level `|`, ignoring pipes inside groups and classes.
    private static func splitTopLevel(_ body: [Character]) -> [String] {
        var branches: [String] = []
        var depth = 0
        var inClass = false
        var start = 0

        for i in body.indices {
            if isEscaped(body, i) { continue }
            let ch = body[i]

            if inClass {
                if ch == "]" { inClass = false }
                continue
            }

            switch ch {
            case "[": inClass = true
            case "(": depth += 1
            case ")": depth -= 1
            case "|" where depth == 0:
                branches.append(String(body[start..<i]))
                start = i + 1
            default: break
            }
        }

        branches.append(String(body[start...]))
        return branches
    }

    private static func matchingParen(_ p: [Character], from open: Int) -> Int? {
        var depth = 0
        var inClass = false

        for i in open..<p.count {
            if isEscaped(p, i) { continue }
            let ch = p[i]

            if inClass {
                if ch == "]" { inClass = false }
                continue
            }

            if ch == "[" {
                inClass = true
            } else if ch == "(" {
                depth += 1
            } else if ch == ")" {
                depth -= 1
                if depth == 0 { return i }
            }
        }
        return nil
    }

    private static func isEscaped(_ p: [Character], _ index: Int) -> Bool {
        var backslashes = 0
        var i = index - 1
        while i >= 0, p[i] == "\\" {
            backslashes += 1
            i -= 1
        }
        return backslashes % 2 == 1
    }
}
