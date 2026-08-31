package dev.shellwright.shell.routing

import java.util.regex.Matcher
import java.util.regex.Pattern
import java.util.regex.PatternSyntaxException

/**
 * A user-supplied pattern that cannot hang the UI thread.
 *
 * Sprint 01 rejects catastrophic patterns at config time
 * (`CFG_REGEX_CATASTROPHIC`), so in principle none reaches a device. The shell
 * defends anyway, for two reasons: a config may have been written before that
 * rule existed, and a pattern that is merely slow rather than exponential still
 * runs on every navigation.
 *
 * The defence is a character-budget interrupt. [Matcher] polls the underlying
 * [CharSequence] as it backtracks, so counting reads gives a cheap, allocation-
 * free ceiling on the work any single match may do — no thread, no timer, and
 * no cost at all on the overwhelming majority of matches, which finish in a few
 * hundred reads.
 */
public class SafeRegex private constructor(
    private val pattern: Pattern,
    private val budget: Int,
) {
    /**
     * Whether [input] matches.
     *
     * Returns false rather than throwing when the budget is exhausted: a rule
     * that cannot be evaluated cheaply must not match, so routing falls through
     * to the next rule and ultimately to the external-browser default.
     */
    public fun matches(input: String): Boolean = try {
        pattern.matcher(BudgetedCharSequence(input, budget)).find()
    } catch (_: BudgetExhausted) {
        false
    }

    /** The original pattern text, for logging. */
    public val source: String get() = pattern.pattern()

    public companion object {
        /**
         * Reads far more than any sane pattern needs on a URL, and far less
         * than an exponential one would.
         */
        private const val DEFAULT_BUDGET = 200_000

        /**
         * Compiles [pattern], or returns null if it does not compile.
         *
         * Compilation happens once, at startup, never per navigation.
         */
        public fun compile(pattern: String, budget: Int = DEFAULT_BUDGET): SafeRegex? = try {
            SafeRegex(Pattern.compile(pattern), budget)
        } catch (_: PatternSyntaxException) {
            null
        }
    }

    /** Raised internally when a single match reads more than its budget. */
    private class BudgetExhausted : RuntimeException(null, null, false, false)

    /**
     * A [CharSequence] that counts reads and gives up past a ceiling.
     *
     * Only [charAt] is budgeted. [length] and [subSequence] are called a
     * bounded number of times and would only add noise to the count.
     */
    private class BudgetedCharSequence(
        private val delegate: CharSequence,
        private val budget: Int,
    ) : CharSequence {
        private var reads = 0

        override val length: Int get() = delegate.length

        override fun get(index: Int): Char {
            if (++reads > budget) throw BudgetExhausted()
            return delegate[index]
        }

        override fun subSequence(startIndex: Int, endIndex: Int): CharSequence =
            BudgetedCharSequence(delegate.subSequence(startIndex, endIndex), budget - reads)

        override fun toString(): String = delegate.toString()
    }
}
