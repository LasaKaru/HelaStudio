import Foundation

/// Decides where every navigation goes.
///
/// Correctness here is what makes the app feel coherent rather than like a
/// browser someone put a tab bar on. It runs on the main thread on every
/// navigation, and a single-page app can fire it hundreds of times in a
/// session.
///
/// - Important: This must reach the same decision as the Kotlin `LinkRouter`
///   for every input. The two are held together by the shared fixture corpus in
///   `tests/fixtures/routing/`, which both test suites read. The behaviour is
///   ported; the code is not.
public final class LinkRouter: @unchecked Sendable {
    private struct Compiled {
        let regex: SafeRegex
        let action: LinkAction
    }

    private let compiled: [Compiled]
    private let downloadExtensions: Set<String>
    private let cache: LRUCache<String, LinkAction>

    /// Patterns that did not compile, so the shell can report them once.
    public let rejectedPatterns: [String]

    public init(
        rules: [LinkRule],
        downloadExtensions: Set<String> = LinkRouter.defaultDownloadExtensions,
        cacheSize: Int = 256
    ) {
        var compiled: [Compiled] = []
        var rejected: [String] = []

        // Compiled once, at construction. Never per navigation.
        for rule in rules {
            if let regex = SafeRegex(rule.pattern) {
                compiled.append(Compiled(regex: regex, action: Self.action(for: rule.action)))
            } else {
                rejected.append(rule.pattern)
            }
        }

        self.compiled = compiled
        self.rejectedPatterns = rejected
        self.downloadExtensions = downloadExtensions
        self.cache = LRUCache(capacity: cacheSize)
    }

    /// Resolves where `url` should open.
    ///
    /// Non-http schemes are decided before any regex runs: `mailto:` belongs to
    /// the mail app whatever the rules say, and evaluating two hundred patterns
    /// against it would be wasted work.
    public func resolve(_ url: String) -> LinkAction {
        if let cached = cache.value(for: url) { return cached }

        let resolved = resolveUncached(url)
        cache.insert(resolved, for: url)
        return resolved
    }

    private func resolveUncached(_ url: String) -> LinkAction {
        if let byScheme = Self.schemeAction(for: url) { return byScheme }
        if isDownload(url) { return .download(url) }

        // First match wins, in declared order. This is what makes the studio's
        // drag-to-reorder meaningful, so it must never become "best match".
        for rule in compiled where rule.regex.matches(url) {
            return rule.action
        }

        // Nothing matched. The studio warns about a missing catch-all
        // (`CFG_LINK_RULE_NO_CATCHALL`); the shell still needs a defined answer,
        // and sending an unrecognised link to the browser is the safe one.
        return .externalBrowser
    }

    /// Actions decided by URL scheme alone, before any pattern is considered.
    static func schemeAction(for url: String) -> LinkAction? {
        guard let colon = url.firstIndex(of: ":") else { return nil }
        let scheme = url[url.startIndex..<colon].lowercased()

        switch scheme {
        case "http", "https":
            return nil
        case "mailto", "tel", "sms", "geo", "maps", "itms-apps", "whatsapp":
            return .external(url)
        // A file URL must never be honoured. See `OriginAllowlist` and the
        // WebView hardening in `WebViewFactory`.
        case "file", "javascript", "data", "about":
            return .block
        default:
            return .external(url)
        }
    }

    private func isDownload(_ url: String) -> Bool {
        let path = url.split(separator: "?", maxSplits: 1)[0]
            .split(separator: "#", maxSplits: 1)[0]

        guard let dot = path.lastIndex(of: ".") else { return false }
        let ext = path[path.index(after: dot)...].lowercased()

        return !ext.isEmpty && downloadExtensions.contains(ext)
    }

    private static func action(for name: String) -> LinkAction {
        switch name {
        case "internal": .internalNavigation
        case "modal": .modal
        case "readerModal": .readerModal
        case "block": .block
        // The schema constrains this to a known set, and a shell at version N
        // may see an action added at version N+1. Treating an unknown action as
        // "open in the browser" degrades gracefully; crashing does not.
        default: .externalBrowser
        }
    }

    public static let defaultDownloadExtensions: Set<String> = [
        "pdf", "zip", "csv", "xlsx", "xls", "docx", "doc", "pptx", "ppt",
        "apk", "dmg", "exe", "mp3", "mp4", "mov", "epub",
    ]
}

/// A minimal insertion-ordered LRU.
///
/// Small enough to be obviously correct, which matters more here than
/// generality: it sits on the navigation hot path.
final class LRUCache<Key: Hashable, Value> {
    private let capacity: Int
    private var storage: [Key: Value] = [:]
    private var order: [Key] = []

    init(capacity: Int) {
        self.capacity = max(1, capacity)
    }

    func value(for key: Key) -> Value? {
        guard let found = storage[key] else { return nil }
        touch(key)
        return found
    }

    func insert(_ value: Value, for key: Key) {
        if storage[key] == nil, storage.count >= capacity, let oldest = order.first {
            storage.removeValue(forKey: oldest)
            order.removeFirst()
        }
        storage[key] = value
        touch(key)
    }

    var count: Int { storage.count }

    private func touch(_ key: Key) {
        if let existing = order.firstIndex(of: key) {
            order.remove(at: existing)
        }
        order.append(key)
    }
}
