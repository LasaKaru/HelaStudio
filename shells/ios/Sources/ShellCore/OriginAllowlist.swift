import Foundation

/// The set of origins treated as the app itself.
///
/// - Important: This is a security boundary, not a convenience. When the
///   JavaScript bridge lands in Sprint 09, a page outside this allowlist must
///   have no bridge object at all — not a bridge that refuses calls, no object
///   (`01_ENGINEERING_STANDARDS.md` §6.2). Enforcement is native, never in JS,
///   because JS running on the page is the thing being defended against.
///
/// Built in Sprint 03 alongside the Android equivalent, before there is
/// anything privileged to gate, so that gating is never something bolted onto a
/// surface that already exists.
public struct OriginAllowlist: Sendable {
    private let allowed: Set<String>

    /// The normalised origins, for logging and diagnostics.
    public var origins: Set<String> { allowed }

    /// Whether any origin was configured at all.
    public var isEmpty: Bool { allowed.isEmpty }

    public init(_ origins: [String]) {
        self.allowed = Set(origins.compactMap(Self.normalize))
    }

    /// Whether `url` is on an origin the app considers its own.
    public func allows(_ url: String?) -> Bool {
        guard let url, !url.isEmpty, let normalized = Self.normalize(url) else { return false }
        return allowed.contains(normalized)
    }

    /// Reduces a URL to `scheme://host[:port]`, or nil if it is not an origin
    /// this app may ever trust.
    private static func normalize(_ value: String) -> String? {
        guard let components = URLComponents(string: value.trimmingCharacters(in: .whitespaces))
        else { return nil }

        // https only. An http origin would let anyone on the network inject a
        // page that then counts as the app's own.
        guard components.scheme?.lowercased() == "https" else { return nil }
        guard let host = components.host?.lowercased(), !host.isEmpty else { return nil }

        guard let port = components.port else { return "https://\(host)" }
        return "https://\(host):\(port)"
    }
}
