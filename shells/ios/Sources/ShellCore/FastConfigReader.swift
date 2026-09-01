import Foundation

/// Phase one of the two-phase config parse.
///
/// - Important: Decoding a 40 KB config through `Codable` costs real
///   milliseconds on a cold launch, on the main thread, before anything is on
///   screen. The startup budget is 300 ms to first frame
///   (`03_TEST_STRATEGY.md` §12) and this must not eat a tenth of it.
///
/// So the first frame is drawn from this: a hand-written scanner that pulls out
/// only the handful of values needed to paint the native skeleton — theme
/// colours, tab labels, the start URL. The full ``ShellConfig`` is decoded on a
/// background queue afterwards and everything else waits for it.
///
/// This reads a *flat subset* deliberately. It is not a JSON parser and must
/// not grow into one; if a field is not needed for the first frame, it belongs
/// in phase two.
///
/// Mirrors the Kotlin `FastConfigReader` field for field.
public enum FastConfigReader {
    /// The values needed to draw the native skeleton before any web content.
    public struct FirstFrame: Sendable, Equatable {
        public let appName: String
        public let initialUrl: String
        public let splashBackground: String
        public let themePrimary: String
        public let themeNavBar: String
        public let themeTabBar: String
        public let statusBarStyle: String
        public let topBarEnabled: Bool
        public let tabBarEnabled: Bool
        public let tabLabels: [String]
    }

    private static let defaultWhite = "#FFFFFF"
    private static let defaultPrimary = "#2563EB"

    /// iOS shows five tabs; more than eight is beyond anything the schema allows.
    private static let maxTabs = 8

    /// Scans the raw config for first-frame values.
    ///
    /// Never throws: a malformed config still has to draw something, and phase
    /// two reports the real error. Every field falls back to the schema default.
    public static func read(_ json: String) -> FirstFrame {
        let scalars = Array(json.unicodeScalars)

        return FirstFrame(
            appName: string(after: "\"name\"", in: scalars) ?? "",
            initialUrl: string(after: "\"initialUrl\"", in: scalars) ?? "",
            splashBackground: string(after: "\"backgroundColor\"", in: scalars) ?? defaultWhite,
            themePrimary: string(after: "\"primary\"", in: scalars) ?? defaultPrimary,
            themeNavBar: string(after: "\"navBar\"", in: scalars) ?? defaultWhite,
            themeTabBar: string(after: "\"tabBar\"", in: scalars) ?? defaultWhite,
            statusBarStyle: string(after: "\"statusBar\"", in: scalars) ?? "dark-content",
            topBarEnabled: enabled(under: "\"topBar\"", in: scalars, default: true),
            tabBarEnabled: enabled(under: "\"tabBar\"", in: scalars, default: false),
            tabLabels: tabLabels(in: scalars)
        )
    }

    /// The string value following the first occurrence of `key`.
    ///
    /// Returns nil when the key is absent, or when its value is not a plain
    /// string — `"tabBar": { … }` must not be mistaken for a colour.
    private static func string(after key: String, in scalars: [Unicode.Scalar]) -> String? {
        guard let keyAt = index(of: key, in: scalars) else { return nil }
        guard let colon = index(of: ":", in: scalars, from: keyAt + key.unicodeScalars.count)
        else { return nil }

        guard let valueAt = firstIndex(from: colon + 1, in: scalars, where: { !isWhitespace($0) })
        else { return nil }

        guard scalars[valueAt] == "\"" else { return nil }
        return readString(from: valueAt, in: scalars)
    }

    /// Reads a JSON string starting at its opening quote, honouring escapes.
    private static func readString(from openQuote: Int, in scalars: [Unicode.Scalar]) -> String? {
        var result = String.UnicodeScalarView()
        var i = openQuote + 1

        while i < scalars.count {
            let scalar = scalars[i]

            if scalar == "\"" {
                return String(result)
            }

            if scalar == "\\" {
                guard i + 1 < scalars.count else { return nil }
                let escaped = scalars[i + 1]
                // Phase one only reads names, URLs, colours, and labels.
                // Unicode escapes are left to phase two rather than
                // reimplementing them here.
                if escaped == "u" { return nil }
                result.append(unescape(escaped))
                i += 2
                continue
            }

            result.append(scalar)
            i += 1
        }

        return nil
    }

    private static func unescape(_ escaped: Unicode.Scalar) -> Unicode.Scalar {
        switch escaped {
        case "n": "\n"
        case "t": "\t"
        case "r": "\r"
        default: escaped
        }
    }

    /// Whether the object introduced by `key` has `"enabled": true`.
    private static func enabled(
        under key: String,
        in scalars: [Unicode.Scalar],
        default fallback: Bool
    ) -> Bool {
        guard let keyAt = index(of: key, in: scalars) else { return fallback }
        guard let enabledAt = index(of: "\"enabled\"", in: scalars, from: keyAt) else { return fallback }
        guard let colon = index(of: ":", in: scalars, from: enabledAt) else { return fallback }

        // Only trust a value that immediately follows the key, so a later
        // object's "enabled" is not mistaken for this one's.
        guard let valueAt = firstIndex(from: colon + 1, in: scalars, where: { !isWhitespace($0) })
        else { return fallback }

        if matches("true", at: valueAt, in: scalars) { return true }
        if matches("false", at: valueAt, in: scalars) { return false }
        return fallback
    }

    /// Tab labels in document order, for painting the bar before web content.
    private static func tabLabels(in scalars: [Unicode.Scalar]) -> [String] {
        guard let tabBarAt = index(of: "\"tabBar\"", in: scalars) else { return [] }
        guard let itemsAt = index(of: "\"items\"", in: scalars, from: tabBarAt) else { return [] }

        let end = index(of: "]", in: scalars, from: itemsAt) ?? scalars.count
        var labels: [String] = []
        var cursor = itemsAt

        while labels.count < maxTabs {
            guard let labelAt = index(of: "\"label\"", in: scalars, from: cursor), labelAt < end
            else { break }
            guard let colon = index(of: ":", in: scalars, from: labelAt) else { break }
            guard let valueAt = firstIndex(from: colon + 1, in: scalars, where: { !isWhitespace($0) })
            else { break }

            // A translated label is an object; phase two resolves it properly.
            if scalars[valueAt] == "\"" {
                if let label = readString(from: valueAt, in: scalars) {
                    labels.append(label)
                }
            } else {
                labels.append("")
            }

            cursor = valueAt + 1
        }

        return labels
    }

    // MARK: - Scalar scanning

    private static func index(
        of needle: String,
        in scalars: [Unicode.Scalar],
        from start: Int = 0
    ) -> Int? {
        let pattern = Array(needle.unicodeScalars)
        guard !pattern.isEmpty, start >= 0 else { return nil }
        guard scalars.count >= pattern.count else { return nil }

        for i in start...(scalars.count - pattern.count) where matchesPattern(pattern, at: i, in: scalars) {
            return i
        }
        return nil
    }

    private static func matches(_ needle: String, at index: Int, in scalars: [Unicode.Scalar]) -> Bool {
        matchesPattern(Array(needle.unicodeScalars), at: index, in: scalars)
    }

    private static func matchesPattern(
        _ pattern: [Unicode.Scalar],
        at index: Int,
        in scalars: [Unicode.Scalar]
    ) -> Bool {
        guard index >= 0, index + pattern.count <= scalars.count else { return false }
        for offset in 0..<pattern.count where scalars[index + offset] != pattern[offset] {
            return false
        }
        return true
    }

    private static func firstIndex(
        from start: Int,
        in scalars: [Unicode.Scalar],
        where predicate: (Unicode.Scalar) -> Bool
    ) -> Int? {
        guard start >= 0 else { return nil }
        for i in start..<scalars.count where predicate(scalars[i]) {
            return i
        }
        return nil
    }

    private static func isWhitespace(_ scalar: Unicode.Scalar) -> Bool {
        scalar == " " || scalar == "\n" || scalar == "\r" || scalar == "\t"
    }
}
