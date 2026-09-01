#if canImport(WebKit)
import Foundation
import ShellCore
import WebKit

/// Handles the parts of the page that need the app's cooperation.
///
/// The file chooser is handled by WebKit itself since iOS 14, which is why this
/// is smaller than its Android counterpart — but the permission strings it
/// depends on must exist in `Info.plist` or the app crashes the moment a user
/// taps a file input. That is enforced in `project.yml`.
public final class ShellUIDelegate: NSObject, WKUIDelegate {

    /// What the host view controller has to do on the page's behalf.
    public protocol Callbacks: AnyObject {
        /// The page asked to open a URL in a new window.
        func openNewWindow(url: String)
        /// The page raised a JavaScript dialog.
        func presentAlert(message: String, completion: @escaping () -> Void)
    }

    private let permissions: Permissions
    private weak var callbacks: (any Callbacks)?

    public init(permissions: Permissions, callbacks: any Callbacks) {
        self.permissions = permissions
        self.callbacks = callbacks
    }

    /// `target="_blank"` and `window.open`.
    ///
    /// Returning nil and routing the URL ourselves keeps every navigation under
    /// the link router. Letting WebKit create the view would open a window the
    /// shell has no chrome for and cannot route.
    public func webView(
        _ webView: WKWebView,
        createWebViewWith configuration: WKWebViewConfiguration,
        for navigationAction: WKNavigationAction,
        windowFeatures: WKWindowFeatures
    ) -> WKWebView? {
        if let url = navigationAction.request.url?.absoluteString {
            callbacks?.openNewWindow(url: url)
        }
        return nil
    }

    public func webView(
        _ webView: WKWebView,
        runJavaScriptAlertPanelWithMessage message: String,
        initiatedByFrame frame: WKFrameInfo,
        completionHandler: @escaping () -> Void
    ) {
        callbacks?.presentAlert(message: message, completion: completionHandler)
    }

    /// Grants only what the configuration declared.
    ///
    /// - Important: The page asks; the config decides. A site requesting the
    ///   camera in an app whose config never enabled it is denied, because the
    ///   app has no `Info.plist` string to justify it and the store listing
    ///   makes no such claim.
    @available(iOS 15.0, *)
    public func webView(
        _ webView: WKWebView,
        requestMediaCapturePermissionFor origin: WKSecurityOrigin,
        initiatedByFrame frame: WKFrameInfo,
        type: WKMediaCaptureType,
        decisionHandler: @escaping (WKPermissionDecision) -> Void
    ) {
        let configured = switch type {
        case .camera: permissions.camera
        case .microphone: permissions.microphone
        case .cameraAndMicrophone: permissions.camera && permissions.microphone
        // Anything not modelled is denied. Defaulting to deny is the only safe
        // direction for a capability the config never mentioned.
        @unknown default: false
        }

        decisionHandler(configured ? .prompt : .deny)
    }
}
#endif
