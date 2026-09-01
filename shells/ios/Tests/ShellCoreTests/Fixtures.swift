import Foundation

/// Locates the shared fixture corpus in `tests/fixtures`.
///
/// The corpus is not copied into the package: both shells and both validators
/// must read the same bytes, so the tests walk up to it instead.
enum Fixtures {
    static let root: URL = {
        var directory = URL(fileURLWithPath: #filePath).deletingLastPathComponent()

        while directory.path != "/" {
            let candidate = directory.appendingPathComponent("tests/fixtures")
            if FileManager.default.fileExists(atPath: candidate.path) {
                return candidate
            }
            directory = directory.deletingLastPathComponent()
        }

        fatalError("Could not locate tests/fixtures from \(#filePath)")
    }()

    static func config(_ name: String) throws -> String {
        try String(contentsOf: root.appendingPathComponent("configs/\(name)"), encoding: .utf8)
    }

    static func routing() throws -> RoutingCorpus {
        let data = try Data(contentsOf: root.appendingPathComponent("routing/link-routing.json"))
        return try JSONDecoder().decode(RoutingCorpus.self, from: data)
    }

    static func regexSafety() throws -> RegexSafetyCorpus {
        let data = try Data(contentsOf: root.appendingPathComponent("regex-safety/patterns.json"))
        return try JSONDecoder().decode(RegexSafetyCorpus.self, from: data)
    }

    static let validConfigs = [
        "minimal.json",
        "maximal.json",
        "all-plugins.json",
        "unicode.json",
        "edge-no-tabs.json",
        "edge-many-tabs.json",
        "edge-long-bundleid.json",
        "edge-many-linkrules.json",
        "edge-single-page.json",
        "edge-deep-nesting.json",
    ]
}

/// The shared routing contract, as read from `tests/fixtures/routing/`.
struct RoutingCorpus: Decodable {
    struct Rule: Decodable {
        let id: String
        let pattern: String
        let action: String
    }

    struct Case: Decodable {
        let why: String
        let rules: String
        let url: String
        let expect: String
        let maxMillis: Double?
    }

    let ruleSets: [String: [Rule]]
    let cases: [Case]
}

/// The shared backtracking-heuristic contract, as read from
/// `tests/fixtures/regex-safety/`.
struct RegexSafetyCorpus: Decodable {
    struct Case: Decodable {
        let pattern: String
        let verdict: String
        let why: String
    }

    let cases: [Case]
}
