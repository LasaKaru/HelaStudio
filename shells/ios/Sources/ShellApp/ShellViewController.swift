#if canImport(UIKit)
import Network
import ShellCore
import UIKit
import WebKit

/// Hosts the web view and the native chrome around it.
///
/// Startup order matters more here than anywhere else, and it is the reverse of
/// the obvious one:
///
/// 1. Read only the first-frame values from the config, synchronously, in under
///    five milliseconds (``FastConfigReader``).
/// 2. Draw the native skeleton — bars, colours, tab labels — and let the launch
///    screen go. The app is now visibly an app.
/// 3. Decode the full config off the main queue, build the web view, and start
///    loading.
///
/// Doing step 3 before step 2 is the difference between an app that appears
/// instantly and one that shows a white rectangle for half a second.
public final class ShellViewController: UIViewController {

    private let firstFrame: FastConfigReader.FirstFrame
    private let configLoader: ConfigLoader
    private let shellVersion: String

    private var config: ShellConfig?
    private var router: LinkRouter?
    private var allowlist = OriginAllowlist([])
    private var navigationDelegate: ShellNavigationDelegate?
    private var uiDelegate: ShellUIDelegate?
    private var authentication: AuthenticationRouter?

    private var webView: WKWebView?
    private let refreshControl = UIRefreshControl()
    private let progressBar = UIProgressView(progressViewStyle: .bar)

    private let pathMonitor = NWPathMonitor()
    private let monitorQueue = DispatchQueue(label: "dev.shellwright.connectivity")
    private var isOnline = true
    private var lastFailedURL: String?

    public init(configLoader: ConfigLoader, shellVersion: String) {
        // Phase one. Cheap enough to block on; everything drawn below needs it.
        self.firstFrame = configLoader.readFirstFrame()
        self.configLoader = configLoader
        self.shellVersion = shellVersion
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("ShellViewController is created in code, never from a nib.")
    }

    public override func viewDidLoad() {
        super.viewDidLoad()

        // Phase two of the startup order: the skeleton, painted from phase one
        // alone, before any web content exists.
        drawSkeleton()

        startConnectivityMonitoring()

        // Phase three. Off the main queue so the skeleton is already on screen.
        configLoader.load { [weak self] result in
            DispatchQueue.main.async {
                switch result {
                case let .success(config):
                    self?.apply(config)
                case .failure:
                    // The skeleton is already up, so the app does not vanish.
                    // It cannot load anything without a config, and says so
                    // rather than showing an empty frame forever.
                    self?.showConfigurationFailure()
                }
            }
        }
    }

    /// Paints the native chrome from the first-frame values.
    private func drawSkeleton() {
        view.backgroundColor = UIColor(hex: firstFrame.splashBackground) ?? .systemBackground

        if firstFrame.topBarEnabled {
            title = firstFrame.appName
            navigationController?.navigationBar.barTintColor = UIColor(hex: firstFrame.themeNavBar)
            navigationController?.navigationBar.tintColor = UIColor(hex: firstFrame.themePrimary)
        }

        progressBar.progressTintColor = UIColor(hex: firstFrame.themePrimary)
        progressBar.isHidden = true
        progressBar.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(progressBar)

        NSLayoutConstraint.activate([
            progressBar.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor),
            progressBar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            progressBar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
        ])
    }

    private func apply(_ config: ShellConfig) {
        self.config = config
        self.router = LinkRouter(rules: config.linkRules)
        self.allowlist = OriginAllowlist(config.app.allowedOrigins)
        self.authentication = AuthenticationRouter(presentationAnchor: view.window)

        let webView = buildWebView(for: config)
        self.webView = webView

        if let url = URL(string: config.app.initialUrl) {
            webView.load(URLRequest(url: url))
        }
    }

    private func buildWebView(for config: ShellConfig) -> WKWebView {
        let webView = WebViewFactory.make(config: config, shellVersion: shellVersion)

        let navigationDelegate = ShellNavigationDelegate(
            router: router ?? LinkRouter(rules: config.linkRules),
            allowlist: allowlist,
            callbacks: self
        )
        let uiDelegate = ShellUIDelegate(permissions: config.permissions, callbacks: self)

        // Held strongly: WKWebView keeps only weak references to its delegates,
        // and a deallocated delegate silently stops routing every navigation.
        self.navigationDelegate = navigationDelegate
        self.uiDelegate = uiDelegate

        webView.navigationDelegate = navigationDelegate
        webView.uiDelegate = uiDelegate
        webView.translatesAutoresizingMaskIntoConstraints = false
        view.insertSubview(webView, belowSubview: progressBar)

        // Constrained to the safe area rather than the full frame, so content
        // does not sit under the Dynamic Island or the home indicator.
        NSLayoutConstraint.activate([
            webView.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor),
            webView.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor),
            webView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
        ])

        if config.webOverrides.pullToRefresh {
            refreshControl.tintColor = UIColor(hex: config.branding.theme.primary)
            refreshControl.addTarget(self, action: #selector(handleRefresh), for: .valueChanged)
            webView.scrollView.refreshControl = refreshControl
        }

        // Overscroll shows the theme colour rather than white.
        webView.scrollView.backgroundColor = UIColor(hex: config.branding.theme.navBar)

        return webView
    }

    @objc private func handleRefresh() {
        webView?.reload()
    }

    private func showConfigurationFailure() {
        title = firstFrame.appName
        progressBar.isHidden = true
    }

    // MARK: - Connectivity

    private func startConnectivityMonitoring() {
        pathMonitor.pathUpdateHandler = { [weak self] path in
            DispatchQueue.main.async {
                let nowOnline = path.status == .satisfied
                let recovered = self?.isOnline == false && nowOnline
                self?.isOnline = nowOnline
                if recovered { self?.retryFailedLoad() }
            }
        }
        pathMonitor.start(queue: monitorQueue)
    }

    /// Reloads what failed, not the offline page the user is looking at —
    /// reloading that would just render the offline page again.
    private func retryFailedLoad() {
        guard let failed = lastFailedURL, let url = URL(string: failed) else { return }
        lastFailedURL = nil
        webView?.load(URLRequest(url: url))
    }

    deinit {
        pathMonitor.cancel()
    }
}

// MARK: - Navigation callbacks

extension ShellViewController: ShellNavigationDelegate.Callbacks {

    public func openExternally(_ action: LinkAction, url: String) {
        guard let target = URL(string: url) else { return }

        // ⚠️ Sign-in must leave the web view entirely. Identity providers
        // refuse to authenticate inside an embedded browser, so routing OAuth
        // through the web view cannot work — see `AuthenticationRouter`.
        if AuthenticationRouter.isAuthenticationURL(url) {
            authentication?.authenticate(url: target, callbackScheme: nil) { [weak self] result in
                if case let .success(callback) = result {
                    DispatchQueue.main.async {
                        self?.webView?.load(URLRequest(url: callback))
                    }
                }
            }
            return
        }

        switch action {
        case .block:
            return
        default:
            // `SFSafariViewController` would keep the user in the app's colour
            // scheme; presenting it is the host app's job in Sprint 04, where
            // the tab controller exists. Until then the system handles it.
            UIApplication.shared.open(target)
        }
    }

    public func pageLoading(url: String) {
        progressBar.isHidden = false
        progressBar.setProgress(0.1, animated: true)
    }

    public func pageFinished(url: String, canGoBack: Bool) {
        lastFailedURL = nil
        progressBar.isHidden = true
        refreshControl.endRefreshing()

        if config?.navigation.topBar.titleSource == "documentTitle" {
            title = webView?.title ?? firstFrame.appName
        }

        // ⚠️ The web view's edge-swipe and the navigation controller's pop
        // gesture both own the left edge. Yielding to the web view while it has
        // history is the behaviour a user expects.
        navigationController?.interactivePopGestureRecognizer?.isEnabled = !canGoBack
    }

    public func networkFailed(url: String) {
        lastFailedURL = url
        progressBar.isHidden = true
        refreshControl.endRefreshing()

        guard config?.offline.enabled == true,
              let page = configLoader.offlinePage()
        else { return }

        let theme = config?.branding.theme
        let rendered = page.render(
            background: config?.branding.splash.backgroundColor ?? "#FFFFFF",
            foreground: "#111827",
            accent: theme?.primary ?? "#2563EB",
            strings: configLoader.offlineStrings()
        )

        webView?.loadHTMLString(rendered, baseURL: OfflinePage.baseURL)
    }

    /// The web content process died. Rebuild rather than leave a blank view.
    public func webContentProcessDied() {
        guard let config else { return }
        let url = webView?.url

        webView?.removeFromSuperview()
        webView = nil

        let replacement = buildWebView(for: config)
        webView = replacement

        if let url {
            replacement.load(URLRequest(url: url))
        }
    }
}

// MARK: - UI callbacks

extension ShellViewController: ShellUIDelegate.Callbacks {

    public func openNewWindow(url: String) {
        // Every navigation stays under the link router, including this one.
        guard let action = router?.resolve(url) else { return }
        openExternally(action, url: url)
    }

    public func presentAlert(message: String, completion: @escaping () -> Void) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "OK", style: .default) { _ in completion() })
        present(alert, animated: true)
    }
}

// MARK: - Colour parsing

extension UIColor {
    /// Parses `#RRGGBB` or `#RRGGBBAA`, returning nil rather than throwing.
    ///
    /// A bad colour must never stop the app from drawing.
    convenience init?(hex: String) {
        let cleaned = hex.hasPrefix("#") ? String(hex.dropFirst()) : hex
        guard cleaned.count == 6 || cleaned.count == 8,
              let value = UInt64(cleaned, radix: 16)
        else { return nil }

        let hasAlpha = cleaned.count == 8
        let red = CGFloat((value >> (hasAlpha ? 24 : 16)) & 0xFF) / 255
        let green = CGFloat((value >> (hasAlpha ? 16 : 8)) & 0xFF) / 255
        let blue = CGFloat((value >> (hasAlpha ? 8 : 0)) & 0xFF) / 255
        let alpha = hasAlpha ? CGFloat(value & 0xFF) / 255 : 1

        self.init(red: red, green: green, blue: blue, alpha: alpha)
    }
}
#endif
