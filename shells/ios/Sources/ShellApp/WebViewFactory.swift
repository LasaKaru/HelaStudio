#if canImport(WebKit)
import Foundation
import ShellCore
import WebKit

/// Builds and hardens the `WKWebView`.
///
/// Most of the value in this file is in the settings that are switched *off*,
/// and in one that is deliberately left alone: `NSAllowsArbitraryLoads` is never
/// added. Sprint 01 rejects `http://` at config time, so App Transport Security
/// can stay strict — and an ATS exception is a flag during App Review.
public enum WebViewFactory {

    /// Shared across every web view in the app.
    ///
    /// A shared process pool is what makes cookies and session storage visible
    /// between the main view and any modal — without it, opening a link in a
    /// modal signs the user out inside that modal, which reads as a bug.
    public static let processPool = WKProcessPool()

    /// Creates a configured web view.
    public static func make(config: ShellConfig, shellVersion: String) -> WKWebView {
        let configuration = WKWebViewConfiguration()

        // `.default()` is the persistent store. `.nonPersistent()` would sign
        // the user out on every cold start, which is the single most common
        // complaint about shells that get this wrong.
        configuration.websiteDataStore = config.webOverrides.persistCookies
            ? .default()
            : .nonPersistent()

        configuration.processPool = processPool
        configuration.allowsInlineMediaPlayback = true
        configuration.mediaTypesRequiringUserActionForPlayback = []
        configuration.defaultWebpagePreferences.allowsContentJavaScript = true

        // WebKit appends this to the agent it composes itself, which makes
        // replacing the base string structurally impossible here — a property
        // the Android side has to achieve by discipline.
        configuration.applicationNameForUserAgent = UserAgent.applicationName(
            shellVersion: shellVersion,
            suffix: config.webOverrides.userAgentSuffix
        )

        configuration.userContentController = userContentController(for: config)

        let webView = WKWebView(frame: .zero, configuration: configuration)

        // The native edge-swipe. Its conflict with the navigation controller's
        // own pop gesture is resolved in `ShellViewController`.
        webView.allowsBackForwardNavigationGestures = true
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.allowsLinkPreview = false

        // Overscroll shows the theme colour rather than white, so rubber-banding
        // past the content does not flash a colour the app never uses.
        webView.isOpaque = false

        return webView
    }

    /// Scripts injected into every page.
    private static func userContentController(for config: ShellConfig) -> WKUserContentController {
        let controller = WKUserContentController()

        if !config.webOverrides.allowZoom {
            // Pinch-zoom is disabled by injecting a viewport rule rather than by
            // setting `scrollView.maximumZoomScale`: the latter also disables
            // the accessibility zoom a user may rely on elsewhere in the system.
            controller.addUserScript(
                WKUserScript(
                    source: Self.disableZoomScript,
                    injectionTime: .atDocumentEnd,
                    forMainFrameOnly: true
                )
            )
        }

        // The dark-mode class the config asks for, so a site can style itself to
        // match the native chrome.
        if config.branding.darkMode != "system" {
            controller.addUserScript(
                WKUserScript(
                    source: Self.colorSchemeScript(config.branding.darkMode),
                    injectionTime: .atDocumentStart,
                    forMainFrameOnly: true
                )
            )
        }

        return controller
    }

    private static let disableZoomScript = """
    var meta = document.querySelector('meta[name=viewport]');
    if (!meta) {
      meta = document.createElement('meta');
      meta.name = 'viewport';
      document.head.appendChild(meta);
    }
    meta.content = 'width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover';
    """

    private static func colorSchemeScript(_ mode: String) -> String {
        """
        document.documentElement.style.colorScheme = '\(mode)';
        document.documentElement.classList.add('shellwright-\(mode)');
        """
    }
}
#endif
