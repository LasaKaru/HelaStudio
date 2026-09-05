import Foundation

/// The page shown when the network, not the site, has failed.
///
/// - Important: Bundled as a resource and never fetched. An offline page loaded
///   over the network is a contradiction, and it is a mistake apps in this
///   category genuinely make.
///
/// Themed at load time rather than at build time, so a colour change stays a
/// content-key change rather than forcing a recompile (ADR 0004).
public struct OfflinePage: Sendable {
    /// The user-facing copy, already localised.
    public struct Strings: Sendable {
        public let title: String
        public let body: String
        public let retry: String

        public init(title: String, body: String, retry: String) {
            self.title = title
            self.body = body
            self.retry = retry
        }
    }

    private let template: String

    public init(template: String) {
        self.template = template
    }

    /// Renders the page with the app's colours and the user's language.
    public func render(
        background: String,
        foreground: String,
        accent: String,
        strings: Strings
    ) -> String {
        template
            .replacingOccurrences(of: "__BACKGROUND__", with: background)
            .replacingOccurrences(of: "__FOREGROUND__", with: foreground)
            .replacingOccurrences(of: "__ACCENT__", with: accent)
            .replacingOccurrences(of: "__TITLE__", with: escape(strings.title))
            .replacingOccurrences(of: "__BODY__", with: escape(strings.body))
            .replacingOccurrences(of: "__RETRY__", with: escape(strings.retry))
    }

    /// The base URL the page is loaded against.
    ///
    /// - Important: `about:blank`, deliberately. Loading it against the site's
    ///   origin would give a bundled asset the site's cookies and storage.
    public static let baseURL = URL(string: "about:blank")

    /// Escapes text destined for HTML.
    ///
    /// The strings are ours, not the user's, but they are localised — and a
    /// translator writing `Vous n'êtes pas connecté` should not be able to
    /// break the page, let alone inject into it.
    private func escape(_ text: String) -> String {
        text
            .replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "'", with: "&#39;")
    }
}
