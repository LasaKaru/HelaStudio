import Foundation

/// Text that is either one string or a map of translations.
///
/// The schema models this as a `oneOf`, which is right for the document but
/// awkward for a typed model, so it is collapsed here into one type. Callers
/// only ever ask for ``resolve(languageTag:)``.
public struct LocalizedText: Codable, Sendable, Equatable {
    /// The text used for any language not listed in ``translations``.
    public let `default`: String
    /// Per-language overrides, keyed by language tag.
    public let translations: [String: String]

    public init(_ value: String, translations: [String: String] = [:]) {
        self.default = value
        self.translations = translations
    }

    /// Returns the best text for a language tag.
    ///
    /// Falls back from an exact match (`en-GB`) to the base language (`en`) to
    /// the default, which is the order a user would expect.
    public func resolve(languageTag: String) -> String {
        if let exact = translations[languageTag] { return exact }
        let base = String(languageTag.prefix(while: { $0 != "-" }))
        return translations[base] ?? `default`
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.singleValueContainer()

        if let plain = try? container.decode(String.self) {
            self.default = plain
            self.translations = [:]
            return
        }

        let entries = try container.decode([String: String].self)
        self.default = entries["default"] ?? ""
        self.translations = entries.filter { $0.key != "default" }
    }

    public func encode(to encoder: any Encoder) throws {
        var container = encoder.singleValueContainer()

        if translations.isEmpty {
            try container.encode(`default`)
            return
        }

        var entries = translations
        entries["default"] = `default`
        try container.encode(entries)
    }
}
