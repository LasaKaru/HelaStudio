#if canImport(WebKit)
import Foundation
import ShellCore
import WebKit

/// Routes navigations and handles failures.
///
/// Two things here are easy to get wrong and embarrassing when you do:
///
/// 1. **Only network-level failures show the offline page.** A 404 from the
///    customer's own site must render *their* 404, not ours.
/// 2. **The web content process can die on its own.** Unhandled, the view goes
///    blank and stays blank — the iOS equivalent of Android's renderer death,
///    and just as common on a memory-pressured device.
public final class ShellNavigationDelegate: NSObject, WKNavigationDelegate {

    /// What the host view controller needs to know about.
    public protocol Callbacks: AnyObject {
        /// Open `url` outside the current web view.
        func openExternally(_ action: LinkAction, url: String)
        /// A main-frame load started.
        func pageLoading(url: String)
        /// A main-frame load finished.
        func pageFinished(url: String, canGoBack: Bool)
        /// The network, not the site, failed. Show the offline page.
        func networkFailed(url: String)
        /// The web content process died and the view must be rebuilt.
        func webContentProcessDied()
    }

    private let router: LinkRouter
    private let allowlist: OriginAllowlist
    private weak var callbacks: (any Callbacks)?

    public init(router: LinkRouter, allowlist: OriginAllowlist, callbacks: any Callbacks) {
        self.router = router
        self.allowlist = allowlist
        self.callbacks = callbacks
    }

    public func webView(
        _ webView: WKWebView,
        decidePolicyFor navigationAction: WKNavigationAction,
        decisionHandler: @escaping (WKNavigationActionPolicy) -> Void
    ) {
        guard let url = navigationAction.request.url?.absoluteString else {
            decisionHandler(.cancel)
            return
        }

        switch router.resolve(url) {
        case .internalNavigation:
            // A rule said "internal" but the allowlist is the security boundary
            // and wins. The validator warns about this at config time
            // (`CFG_ORIGIN_NOT_COVERED`).
            if allowlist.allows(url) {
                decisionHandler(.allow)
            } else {
                callbacks?.openExternally(.externalBrowser, url: url)
                decisionHandler(.cancel)
            }

        case .block:
            decisionHandler(.cancel)

        case let other:
            callbacks?.openExternally(other, url: url)
            decisionHandler(.cancel)
        }
    }

    public func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        callbacks?.pageLoading(url: webView.url?.absoluteString ?? "")
    }

    public func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        callbacks?.pageFinished(url: webView.url?.absoluteString ?? "", canGoBack: webView.canGoBack)
    }

    public func webView(
        _ webView: WKWebView,
        didFailProvisionalNavigation navigation: WKNavigation!,
        withError error: any Error
    ) {
        // A provisional navigation failure means nothing was reached. That is
        // the only case the offline page is for.
        guard Self.isNetworkLevel(error) else { return }

        let failed = (error as NSError).userInfo[NSURLErrorFailingURLStringErrorKey] as? String
        callbacks?.networkFailed(url: failed ?? webView.url?.absoluteString ?? "")
    }

    public func webView(
        _ webView: WKWebView,
        didFail navigation: WKNavigation!,
        withError error: any Error
    ) {
        guard Self.isNetworkLevel(error) else { return }
        callbacks?.networkFailed(url: webView.url?.absoluteString ?? "")
    }

    public func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        callbacks?.webContentProcessDied()
    }

    /// Whether an error means the network failed rather than the site.
    ///
    /// Notably absent: anything implying a server responded. An HTTP status
    /// arrives as a successful navigation, so it never reaches here at all.
    static func isNetworkLevel(_ error: any Error) -> Bool {
        let nsError = error as NSError
        guard nsError.domain == NSURLErrorDomain else { return false }

        return [
            NSURLErrorNotConnectedToInternet,
            NSURLErrorNetworkConnectionLost,
            NSURLErrorCannotFindHost,
            NSURLErrorCannotConnectToHost,
            NSURLErrorTimedOut,
            NSURLErrorDNSLookupFailed,
            NSURLErrorSecureConnectionFailed,
            NSURLErrorInternationalRoamingOff,
            NSURLErrorDataNotAllowed,
        ].contains(nsError.code)
    }
}
#endif
