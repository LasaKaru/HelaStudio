package dev.shellwright.shell.routing

import java.util.regex.Pattern
import java.util.regex.PatternSyntaxException

/** Whether a user-supplied pattern is safe to run on every navigation. */
public sealed interface RegexVerdict {
    /** Compiles, and shows none of the shapes that backtrack exponentially. */
    public data object Ok : RegexVerdict

    /** Does not compile. */
    public data object Invalid : RegexVerdict

    /**
     * Compiles, but contains [construct] — a quantified group whose body is
     * itself ambiguous, as in `(a+)+`.
     */
    public data class Catastrophic(val construct: String) : RegexVerdict
}

/**
 * Detects patterns that would freeze the app on every navigation.
 *
 * This is a port of the studio's `checkRegex`
 * (`packages/config-schema/src/rules/regex-safety.ts`), and it must agree with
 * it, with the C# validator, and with the iOS shell for every input. The shared
 * corpus in `tests/fixtures/regex-safety/` is what holds the four together.
 *
 * It is a deliberately conservative structural scan rather than a full parse.
 * It catches the shapes seen in practice without needing to model an engine,
 * and — just as importantly — it leaves alone the shapes that look dangerous
 * but are not.
 *
 * On Android this is the *first* line of defence rather than the only one:
 * [SafeRegex] still budgets every match, because `java.util.regex` lets it. iOS
 * has no such option, which is why refusing the pattern up front has to work on
 * its own. Keeping both shells on the same check means a customer sees the same
 * rule ignored on both platforms rather than one silently working.
 */
public object BacktrackingCheck {

    /** Classifies [pattern]. */
    public fun verdict(pattern: String): RegexVerdict {
        try {
            Pattern.compile(pattern)
        } catch (_: PatternSyntaxException) {
            return RegexVerdict.Invalid
        }

        val construct = findNestedQuantifier(pattern)
        return if (construct == null) RegexVerdict.Ok else RegexVerdict.Catastrophic(construct)
    }

    /**
     * Quantifiers that repeat a group. `?` is excluded: it tries the group at
     * most once and so cannot compound.
     */
    private val QUANTIFIERS = setOf('*', '+', '{')

    /**
     * Finds a quantified group whose body is itself quantified or alternated —
     * the `(a+)+` and `(a|a)*` shapes that cause exponential backtracking.
     *
     * Returns the offending substring, or null when the pattern looks safe.
     */
    private fun findNestedQuantifier(pattern: String): String? {
        for (i in pattern.indices) {
            if (pattern[i] != '(' || isEscaped(pattern, i)) continue

            val close = matchingParen(pattern, i) ?: continue
            val afterIndex = close + 1
            if (afterIndex >= pattern.length) continue
            if (pattern[afterIndex] !in QUANTIFIERS) continue

            // A lazy or possessive quantifier bounds the search.
            val modifier = pattern.getOrNull(afterIndex + 1)
            if (modifier == '?' || modifier == '+') continue

            if (bodyIsAmbiguous(pattern.substring(i + 1, close))) {
                return pattern.substring(i, afterIndex + 1)
            }
        }
        return null
    }

    /**
     * True when a group body can match the same text more than one way.
     *
     * Two shapes cause exponential backtracking when wrapped in an outer
     * repetition:
     *
     *   1. The body *begins* with a quantified atom, as in `(a+)+`. The inner
     *      and outer repetitions then compete for the same characters.
     *   2. The body is a top-level alternation whose branches overlap, as in
     *      `(a|a)*` or `(a|ab)*`.
     *
     * Note what is deliberately *not* flagged: `(-[a-z]+)*`, the ordinary
     * separated-list idiom. Its body must consume a literal `-` before anything
     * else, so repetitions cannot overlap and matching stays linear. Rejecting
     * it would be a false positive on one of the most common patterns users
     * write.
     */
    private fun bodyIsAmbiguous(body: String): Boolean {
        val inner = stripGroupPrefix(body)
        return startsWithQuantifiedAtom(inner) || hasOverlappingAlternation(inner)
    }

    /** Removes a non-capturing, lookaround, or named-group prefix such as `?:`. */
    private fun stripGroupPrefix(body: String): String {
        if (!body.startsWith("?")) return body
        val rest = body.substring(1)

        return when {
            rest.startsWith(":") || rest.startsWith("=") || rest.startsWith("!") -> rest.substring(1)
            // `?<=` and `?<!` are lookbehind; `?<name>` is a named group.
            rest.startsWith("<=") || rest.startsWith("<!") -> rest.substring(2)
            rest.startsWith("<") -> {
                val close = rest.indexOf('>')
                if (close == -1) body else rest.substring(close + 1)
            }
            else -> body
        }
    }

    /** True when the first atom of [body] carries an unbounded quantifier. */
    private fun startsWithQuantifiedAtom(body: String): Boolean {
        val end = firstAtomEnd(body) ?: return false
        if (end >= body.length) return false

        return when (body[end]) {
            '*', '+' -> true
            // `{n,}` is unbounded; `{n}` and `{n,m}` are not, so they cannot
            // explode.
            '{' -> {
                val close = body.indexOf('}', end)
                close != -1 && body.substring(end + 1, close).endsWith(",")
            }
            else -> false
        }
    }

    /** Index just past the first atom in [body], or null if there is none. */
    private fun firstAtomEnd(body: String): Int? {
        val first = body.firstOrNull() ?: return null

        return when (first) {
            '\\' -> if (body.length > 1) 2 else null
            '[' -> classEnd(body)?.plus(1)
            '(' -> matchingParen(body, 0)?.plus(1)
            // A quantifier cannot open an atom, and an anchor consumes nothing.
            '*', '+', '?', '{', '|', ')', '^', '$' -> null
            else -> 1
        }
    }

    /** End index of a character class starting at position 0, or null. */
    private fun classEnd(body: String): Int? {
        for (i in 1 until body.length) {
            if (isEscaped(body, i)) continue
            if (body[i] == ']') return i
        }
        return null
    }

    /**
     * True when two top-level branches can match the same text.
     *
     * Only identical branches, or one branch that is a prefix of another, are
     * genuinely ambiguous. `(-a|-b)` merely shares a first character and stays
     * linear, so it is left alone.
     */
    private fun hasOverlappingAlternation(body: String): Boolean {
        val branches = splitTopLevel(body)
        if (branches.size < 2) return false

        for (i in branches.indices) {
            for (j in i + 1 until branches.size) {
                val a = branches[i]
                val b = branches[j]
                if (a.startsWith(b) || b.startsWith(a)) return true
            }
        }
        return false
    }

    /** Splits a body on top-level `|`, ignoring pipes inside groups and classes. */
    private fun splitTopLevel(body: String): List<String> {
        val branches = mutableListOf<String>()
        var depth = 0
        var inClass = false
        var start = 0

        for (i in body.indices) {
            if (isEscaped(body, i)) continue
            val ch = body[i]

            if (inClass) {
                if (ch == ']') inClass = false
                continue
            }

            when {
                ch == '[' -> inClass = true
                ch == '(' -> depth++
                ch == ')' -> depth--
                ch == '|' && depth == 0 -> {
                    branches.add(body.substring(start, i))
                    start = i + 1
                }
            }
        }

        branches.add(body.substring(start))
        return branches
    }

    private fun matchingParen(pattern: String, open: Int): Int? {
        var depth = 0
        var inClass = false

        for (i in open until pattern.length) {
            if (isEscaped(pattern, i)) continue
            val ch = pattern[i]

            if (inClass) {
                if (ch == ']') inClass = false
                continue
            }

            when (ch) {
                '[' -> inClass = true
                '(' -> depth++
                ')' -> if (--depth == 0) return i
            }
        }
        return null
    }

    private fun isEscaped(s: String, index: Int): Boolean {
        var backslashes = 0
        var i = index - 1
        while (i >= 0 && s[i] == '\\') {
            backslashes++
            i--
        }
        return backslashes % 2 == 1
    }
}
