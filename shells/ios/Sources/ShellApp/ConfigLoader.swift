#if canImport(UIKit)
import Foundation
import ShellCore
import UIKit

/// Loads the embedded configuration and the offline page.
///
/// Two phases, because the first frame cannot wait for the second:
/// ``readFirstFrame()`` is synchronous and cheap enough for the main thread;
/// ``load(completion:)`` decodes the full model off it.
public struct ConfigLoader: Sendable {

    private let bundle: Bundle
    private let configName: String

    public init(bundle: Bundle = .main, configName: String = "appconfig") {
        self.bundle = bundle
        self.configName = configName
    }

    /// Phase one. Safe to call on the main thread.
    public func readFirstFrame() -> FastConfigReader.FirstFrame {
        FastConfigReader.read(rawConfig() ?? "")
    }

    /// Phase two. Never call this on the main thread.
    public func load(completion: @escaping @Sendable (Result<ShellConfig, any Error>) -> Void) {
        DispatchQueue.global(qos: .userInitiated).async {
            guard let raw = rawConfig() else {
                completion(.failure(LoaderError.missingConfiguration))
                return
            }

            do {
                let config = try ShellJSON.decoder().decode(ShellConfig.self, from: Data(raw.utf8))
                completion(.success(config))
            } catch {
                completion(.failure(error))
            }
        }
    }

    /// The bundled offline page template.
    public func offlinePage() -> OfflinePage? {
        guard let url = bundle.url(forResource: "offline", withExtension: "html"),
              let template = try? String(contentsOf: url, encoding: .utf8)
        else { return nil }

        return OfflinePage(template: template)
    }

    /// Offline copy in the user's language.
    ///
    /// Localised because an error message in the wrong language is worse than
    /// no message.
    public func offlineStrings() -> OfflinePage.Strings {
        OfflinePage.Strings(
            title: NSLocalizedString(
                "offline.title",
                value: "You are offline",
                comment: "Shown when the device has no connection"
            ),
            body: NSLocalizedString(
                "offline.body",
                value: "This page needs a connection. It will load as soon as you are back online.",
                comment: "Explains the offline screen"
            ),
            retry: NSLocalizedString(
                "offline.retry",
                value: "Try again",
                comment: "Button that reloads the failed page"
            )
        )
    }

    private func rawConfig() -> String? {
        guard let url = bundle.url(forResource: configName, withExtension: "json") else { return nil }
        return try? String(contentsOf: url, encoding: .utf8)
    }

    enum LoaderError: Error {
        case missingConfiguration
    }
}
#endif
