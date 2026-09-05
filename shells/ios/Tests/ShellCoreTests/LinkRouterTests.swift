import Foundation
import Testing
@testable import ShellCore

/// Behaviour the shared corpus does not cover, because it is iOS-specific or
/// concerns the router's internals rather than its decisions.
struct LinkRouterTests {

    private func router(_ rules: (String, String)...) -> LinkRouter {
        LinkRouter(
            rules: rules.enumerated().map { index, rule in
                LinkRule(id: "rule-\(index)", pattern: rule.0, action: rule.1)
            }
        )
    }

    @Test("an uncompilable pattern is reported so the shell can surface it once")
    func rejectedPatternsAreReported() {
        let broken = router(("^https://app\\.acme\\.com(", "internal"), (".*", "externalBrowser"))

        #expect(broken.rejectedPatterns.count == 1)
        #expect(broken.rejectedPatterns.first == "^https://app\\.acme\\.com(")
    }

    @Test("repeated resolutions of the same url are served from the cache")
    func resolutionsAreCached() {
        let busy = router((".*", "internal"))
        let url = "https://app.acme.com/orders"

        let first = measure { _ = busy.resolve(url) }
        let cached = measure { for _ in 0..<1_000 { _ = busy.resolve(url) } } / 1_000

        // Compared against the first call rather than a fixed number, so this
        // stays stable on a loaded CI machine.
        #expect(cached < first)
    }

    @Test("a 200-rule config resolves inside the per-navigation budget")
    func manyRulesStayFast() {
        var rules = (1..<200).map {
            LinkRule(id: "rule-\($0)", pattern: "^https://app\\.acme\\.com/section-\($0)/", action: "internal")
        }
        rules.append(LinkRule(id: "fallback", pattern: ".*", action: "externalBrowser"))

        let busy = LinkRouter(rules: rules)
        let urls = (1...100).map { "https://app.acme.com/section-\($0)/page-\($0)" }

        // Warm without populating the cache: each pass uses distinct URLs.
        for pass in 0..<5 {
            for url in urls { _ = busy.resolve("\(url)?warm=\(pass)") }
        }

        let resolutions = 10_000
        let elapsed = measure {
            for pass in 0..<(resolutions / urls.count) {
                for url in urls { _ = busy.resolve("\(url)?run=\(pass)") }
            }
        }

        let meanMillis = elapsed / Double(resolutions) * 1000
        #expect(meanMillis < 1.0, "mean \(meanMillis) ms against a 1 ms budget")
    }

    @Test("the cache does not grow without bound")
    func cacheIsBounded() {
        let cache = LRUCache<String, Int>(capacity: 4)

        for i in 0..<100 { cache.insert(i, for: "key-\(i)") }

        #expect(cache.count == 4)
        #expect(cache.value(for: "key-0") == nil)
        #expect(cache.value(for: "key-99") == 99)
    }

    private func measure(_ work: () -> Void) -> TimeInterval {
        let started = Date()
        work()
        return Date().timeIntervalSince(started)
    }
}
