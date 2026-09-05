import Foundation
import Testing
@testable import ShellCore

/// The routing contract between the two shells.
///
/// The Android and iOS routers share no code — the behaviour is ported, the
/// source is not — so this corpus is the only thing that catches them drifting
/// apart. The Kotlin suite reads the same file and asserts the same decisions.
///
/// It is the same technique that holds the TypeScript and C# validators
/// together, applied to the second place in the system where one behaviour has
/// two implementations.
struct LinkRoutingContractTests {

    @Test("every case in the shared corpus routes as declared", arguments: try! Fixtures.routing().cases)
    func sharedCorpus(testCase: RoutingCorpus.Case) throws {
        let corpus = try Fixtures.routing()

        let rules = try #require(
            corpus.ruleSets[testCase.rules],
            "the corpus names a rule set '\(testCase.rules)' that it does not define"
        )

        let router = LinkRouter(rules: rules.map { LinkRule(id: $0.id, pattern: $0.pattern, action: $0.action) })

        let started = Date()
        let resolved = router.resolve(testCase.url)
        let elapsedMillis = Date().timeIntervalSince(started) * 1000

        #expect(
            resolved.fixtureName == testCase.expect,
            "\(testCase.url) — \(testCase.why)"
        )

        if let budget = testCase.maxMillis {
            #expect(
                elapsedMillis < budget,
                "routing took \(elapsedMillis) ms against a \(budget) ms budget — \(testCase.why)"
            )
        }
    }

    @Test("the corpus covers every action the router can return")
    func corpusCoverage() throws {
        let corpus = try Fixtures.routing()
        let covered = Set(corpus.cases.map(\.expect))

        // A router action with no fixture is an untested decision, and the
        // other shell has nothing to be held to.
        let expected: Set<String> = [
            "internal", "readerModal", "externalBrowser", "block", "external", "download",
        ]

        #expect(
            expected.isSubset(of: covered),
            "actions with no case in the shared corpus: \(expected.subtracting(covered).sorted())"
        )
    }
}
