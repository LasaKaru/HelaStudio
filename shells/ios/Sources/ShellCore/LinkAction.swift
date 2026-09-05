import Foundation

/// Where a link opens.
///
/// Mirrors the Kotlin `LinkAction`. The two are checked against each other by
/// the shared fixture corpus in `tests/fixtures/routing/`.
public enum LinkAction: Sendable, Equatable {
    /// Load in the current web view.
    case internalNavigation
    /// Load in a modal web view stacked over the current one.
    case modal
    /// Load in a modal with reader styling applied.
    case readerModal
    /// Hand to `SFSafariViewController`.
    case externalBrowser
    /// Refuse the navigation entirely.
    case block
    /// Hand to another app: `mailto:`, `tel:`, `sms:`.
    case external(String)
    /// Hand to the download flow.
    case download(String)

    /// The stable name used in the shared routing fixtures.
    ///
    /// Deliberately not derived from the case name: the fixture corpus is the
    /// contract between this shell and the Android one, so the strings are
    /// written out rather than left to a reflection detail that could change.
    public var fixtureName: String {
        switch self {
        case .internalNavigation: "internal"
        case .modal: "modal"
        case .readerModal: "readerModal"
        case .externalBrowser: "externalBrowser"
        case .block: "block"
        case .external: "external"
        case .download: "download"
        }
    }
}
