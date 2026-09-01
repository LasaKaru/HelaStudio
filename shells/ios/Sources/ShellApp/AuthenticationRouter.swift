#if canImport(AuthenticationServices)
import AuthenticationServices
import Foundation
import ShellCore

/// Runs sign-in flows outside the web view.
///
/// - Important: This is the single highest-value iOS-specific fix in the shell
///   (`RT-08` in the master spec). Identity providers detect embedded browsers
///   and refuse to authenticate in them — Google has blocked WKWebView sign-in
///   outright — so a shell that routes OAuth through its own web view simply
///   cannot log users in, and the failure looks like the customer's bug.
///
/// `ASWebAuthenticationSession` is the sanctioned path. It shares Safari's
/// cookie jar, so a user already signed in to the provider is not asked again,
/// and providers accept it.
public final class AuthenticationRouter: NSObject {

    /// Hosts that should be handed to the authentication session rather than
    /// loaded in the web view.
    ///
    /// Deliberately a list rather than a heuristic: guessing wrong in the
    /// permissive direction sends ordinary pages out to a system sheet, and
    /// guessing wrong in the other direction breaks sign-in silently.
    public static let knownProviderHosts: Set<String> = [
        "accounts.google.com",
        "appleid.apple.com",
        "www.facebook.com",
        "login.microsoftonline.com",
        "github.com",
        "auth0.com",
        "okta.com",
    ]

    /// Whether `url` looks like a provider sign-in page.
    public static func isAuthenticationURL(_ url: String) -> Bool {
        guard let host = URLComponents(string: url)?.host?.lowercased() else { return false }

        if knownProviderHosts.contains(host) { return true }

        // A provider's own subdomains count: `login.okta.com`, `dev-1.auth0.com`.
        return knownProviderHosts.contains { host.hasSuffix(".\($0)") }
    }

    private var session: ASWebAuthenticationSession?
    private weak var presentationAnchor: ASPresentationAnchor?

    public init(presentationAnchor: ASPresentationAnchor?) {
        self.presentationAnchor = presentationAnchor
    }

    /// Starts a sign-in flow.
    ///
    /// - Parameter callbackScheme: the app's custom URL scheme, from
    ///   `config.deepLinks.customScheme`. Without it the session cannot know
    ///   when the provider has redirected back.
    public func authenticate(
        url: URL,
        callbackScheme: String?,
        completion: @escaping (Result<URL, any Error>) -> Void
    ) {
        let session = ASWebAuthenticationSession(
            url: url,
            callbackURLScheme: callbackScheme
        ) { callbackURL, error in
            if let callbackURL {
                completion(.success(callbackURL))
            } else if let error {
                completion(.failure(error))
            }
        }

        session.presentationContextProvider = self
        // Shares Safari's cookies, so a user already signed in to the provider
        // is not made to sign in again.
        session.prefersEphemeralWebBrowserSession = false

        self.session = session
        session.start()
    }
}

extension AuthenticationRouter: ASWebAuthenticationPresentationContextProviding {
    public func presentationAnchor(for session: ASWebAuthenticationSession) -> ASPresentationAnchor {
        presentationAnchor ?? ASPresentationAnchor()
    }
}
#endif
