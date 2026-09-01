import Foundation

/// Builds the web view's user agent.
///
/// - Important: **Append, never replace.** Replacing the whole string breaks
///   feature detection on the customer's own site — their analytics stop
///   recognising the browser, their polyfill decisions go wrong, and their CDN
///   may serve a desktop layout. It is one of the highest-volume support
///   tickets in this category, and entirely avoidable.
///
/// On iOS this is what `WKWebViewConfiguration.applicationNameForUserAgent`
/// exists for: WebKit appends it rather than letting you overwrite the base.
public enum UserAgent {
    /// The token every Shellwright app carries, whatever else is appended.
    public static let shellToken = "Shellwright"

    /// The value for `applicationNameForUserAgent`.
    ///
    /// WebKit composes the full agent itself, so only the suffix is supplied —
    /// which makes replacing the base string structurally impossible here, a
    /// property the Android side has to achieve by discipline.
    public static func applicationName(shellVersion: String, suffix: String?) -> String {
        var parts = ["\(shellToken)/\(shellVersion)"]

        if let suffix = suffix?.trimmingCharacters(in: .whitespaces), !suffix.isEmpty {
            parts.append(suffix)
        }

        return parts.joined(separator: " ")
    }

    /// Returns `base` with the shell token and any configured suffix appended.
    ///
    /// Used where the full string is composed by hand rather than by WebKit,
    /// and to keep the two shells' output comparable.
    public static func build(base: String, shellVersion: String, suffix: String?) -> String {
        let trimmed = base.trimmingCharacters(in: .whitespaces)
        return "\(trimmed) \(applicationName(shellVersion: shellVersion, suffix: suffix))"
    }

    /// Whether a user agent string came from a Shellwright app.
    public static func isShell(_ userAgent: String) -> Bool {
        userAgent.contains("\(shellToken)/")
    }
}
