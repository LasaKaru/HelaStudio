import Foundation

/// A user-supplied pattern that cannot hang the UI thread.
///
/// Sprint 01 rejects catastrophic patterns at config time
/// (`CFG_REGEX_CATASTROPHIC`), so in principle none reaches a device. The shell
/// defends anyway: a config may have been built before that rule existed, and
/// old builds keep running long after the studio has been fixed.
///
/// - Note: The two shells defend by different means, and the difference is
///   forced rather than chosen. Android interrupts a runaway match by counting
///   the reads `java.util.regex` makes against the `CharSequence` it was handed,
///   so it can afford to compile anything and stop it later.
///   `NSRegularExpression` is ICU-backed and does not yield while it
///   backtracks — the block passed to `enumerateMatches` is called for matches,
///   not for progress — so there is no moment at which a deadline could be
///   observed. Once a match starts it runs to completion or the watchdog kills
///   the app. The only place iOS can intervene is before the pattern is ever
///   run, which is what [`BacktrackingCheck`] is for.
///
///   Both shells must nevertheless reach the same *decision*: a refused pattern
///   and an exhausted budget both mean "this rule does not match". The shared
///   corpora in `tests/fixtures/routing/` and `tests/fixtures/regex-safety/`
///   are what check that.
public struct SafeRegex: Sendable {
    private let regex: NSRegularExpression

    /// The original pattern text, for logging.
    public var source: String { regex.pattern }

    /// Compiles `pattern`, or returns nil if it does not compile or is unsafe
    /// to run.
    ///
    /// Compilation happens once, at startup, never per navigation. A nil here
    /// is reported by `LinkRouter.rejectedPatterns` and the rule is skipped, so
    /// routing falls through to the next rule and ultimately to the
    /// external-browser default.
    public init?(_ pattern: String) {
        guard case .ok = BacktrackingCheck.verdict(for: pattern) else { return nil }
        guard let compiled = try? NSRegularExpression(pattern: pattern) else { return nil }
        self.regex = compiled
    }

    /// Whether `input` matches.
    public func matches(_ input: String) -> Bool {
        let range = NSRange(input.startIndex..<input.endIndex, in: input)
        return regex.firstMatch(in: input, options: [], range: range) != nil
    }
}
