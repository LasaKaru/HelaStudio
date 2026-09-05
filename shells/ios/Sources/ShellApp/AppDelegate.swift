#if canImport(UIKit)
import ShellCore
import UIKit

/// The application entry point.
///
/// - Important: Deliberately almost empty. Every millisecond spent in
///   `didFinishLaunching` is a millisecond of blank screen against a 300 ms
///   budget to first frame, and `01_ENGINEERING_STANDARDS.md` §10 bans work
///   here for exactly that reason. When plugins arrive in Sprint 10 they
///   register lazily on first bridge call — fifteen plugins initialising here
///   would be a two-second cold start.
@main
public final class AppDelegate: UIResponder, UIApplicationDelegate {

    public var window: UIWindow?

    public func application(
        _ application: UIApplication,
        didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]? = nil
    ) -> Bool {
        let shell = ShellViewController(
            configLoader: ConfigLoader(),
            shellVersion: Self.shellVersion
        )

        let navigation = UINavigationController(rootViewController: shell)

        let window = UIWindow(frame: UIScreen.main.bounds)
        window.rootViewController = navigation
        window.makeKeyAndVisible()
        self.window = window

        return true
    }

    /// The shell template version, appended to the user agent so a site can
    /// branch on it. Rewritten by code generation in Sprint 04.
    static let shellVersion: String =
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0.0"
}
#endif
