import Foundation
import Testing
@testable import ShellCore

/// The backtracking-heuristic contract, shared with the studio, the API, and
/// the Android shell.
///
/// Four implementations of one judgement, sharing no code. If they disagree, a
/// customer's rule either stops working silently or freezes their app — see
/// `tests/fixtures/regex-safety/README.md`.
struct BacktrackingCheckTests {

    @Test("every pattern in the shared corpus is classified as declared",
          arguments: try! Fixtures.regexSafety().cases)
    func sharedCorpus(testCase: RegexSafetyCorpus.Case) {
        let verdict = BacktrackingCheck.verdict(for: testCase.pattern)

        let name: String = switch verdict {
        case .ok: "ok"
        case .invalid: "invalid"
        case .catastrophic: "catastrophic"
        }

        #expect(name == testCase.verdict, "/\(testCase.pattern)/ — \(testCase.why)")
    }

    @Test("a rejection names the construct that caused it")
    func namesTheConstruct() {
        guard case .catastrophic(let construct) = BacktrackingCheck.verdict(for: "^(a+)+$") else {
            Issue.record("^(a+)+$ should be rejected")
            return
        }

        // The studio surfaces this text to the user, so it has to point at the
        // part of their pattern that is wrong rather than repeat the whole thing.
        #expect(construct == "(a+)+")
    }

    @Test("the empty pattern is refused, which ICU decides for us")
    func emptyPattern() {
        // The one place the four engines disagree: JavaScript, Java and .NET
        // all compile "" as a match-everything, ICU rejects it. It is not in
        // the shared corpus for that reason. Nothing is lost — the schema's
        // `UrlPattern` sets minLength 1, so an empty pattern never reaches a
        // shell, and refusing is the safer of the two readings anyway.
        #expect(BacktrackingCheck.verdict(for: "") == .invalid)
    }

    @Test("the scan itself cannot be made slow")
    func scanIsLinear() {
        // A checker that hangs on a hostile pattern has only moved the problem.
        let hostile = String(repeating: "(", count: 2000) + String(repeating: ")*", count: 2000)

        let started = Date()
        _ = BacktrackingCheck.verdict(for: hostile)
        let elapsedMillis = Date().timeIntervalSince(started) * 1000

        #expect(elapsedMillis < 500, "the scan took \(elapsedMillis) ms")
    }
}

/// What `SafeRegex` does with the verdict.
struct SafeRegexTests {

    @Test("an ordinary pattern matches normally")
    func ordinaryPattern() throws {
        let regex = try #require(SafeRegex(#"^https://app\.acme\.com"#))

        #expect(regex.matches("https://app.acme.com/orders"))
        #expect(!regex.matches("https://other.example.com/"))
    }

    @Test("a pattern that does not compile is refused rather than throwing")
    func uncompilable() {
        #expect(SafeRegex("(") == nil)
        #expect(SafeRegex("[z-a]") == nil)
    }

    @Test("an exponential pattern is refused before it can ever run")
    func exponentialPattern() {
        // This is the whole reason the check exists on iOS. There is no second
        // line of defence: if this returned a usable regex, the input below
        // would spin ICU until the watchdog killed the app.
        #expect(SafeRegex("^(a+)+$") == nil)
    }

    @Test("the separated-list idiom is not penalised")
    func separatedList() throws {
        // The pattern Sprint 01's checker was corrected not to reject.
        let regex = try #require(SafeRegex(#"^[a-z]+(-[a-z]+)*$"#))

        #expect(regex.matches("some-long-hyphenated-slug"))
    }

    @Test("a long legitimate url is matched without complaint")
    func longUrl() throws {
        let regex = try #require(SafeRegex(#"^https://app\.acme\.com/.*"#))
        let long = "https://app.acme.com/" + String(repeating: "segment/", count: 200)

        #expect(regex.matches(long))
    }

    @Test("the source pattern is available for logging")
    func sourceIsPreserved() throws {
        let pattern = #"^https://app\.acme\.com"#

        #expect(try #require(SafeRegex(pattern)).source == pattern)
    }
}
