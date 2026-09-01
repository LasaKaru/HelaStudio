import Foundation
import Testing
@testable import ShellCore

/// The origin allowlist, which the JavaScript bridge will be gated on in
/// Sprint 09. Mirrors the Android `OriginAllowlistTest` case for case: the two
/// shells must make the same trust decision about the same URL.
struct OriginAllowlistTests {

    private let allowlist = OriginAllowlist([
        "https://app.acme.com",
        "https://acme.com",
        "https://staging.acme.com:8443",
    ])

    @Test("a url on an allowed origin is allowed, whatever its path")
    func allowedOrigin() {
        #expect(allowlist.allows("https://app.acme.com"))
        #expect(allowlist.allows("https://app.acme.com/orders/42?ref=x#top"))
    }

    @Test("a different host is not allowed")
    func differentHost() {
        #expect(!allowlist.allows("https://evil.example.com/"))
    }

    /// A prefix match would allow this. An origin comparison does not.
    @Test("a host that merely starts with an allowed host is not allowed")
    func prefixIsNotEnough() {
        #expect(!allowlist.allows("https://app.acme.com.evil.example/"))
    }

    @Test("a subdomain of an allowed host is not implicitly allowed")
    func subdomainNotImplied() {
        #expect(!allowlist.allows("https://internal.app.acme.com/"))
    }

    /// The whole point of the allowlist. An http origin would let anyone on the
    /// network inject a page that counts as the app's own.
    @Test("the same host over plain http is not allowed")
    func cleartextRefused() {
        #expect(!allowlist.allows("http://app.acme.com/"))
    }

    @Test("port is part of the origin")
    func portMatters() {
        #expect(allowlist.allows("https://staging.acme.com:8443/x"))
        #expect(!allowlist.allows("https://staging.acme.com/x"))
        #expect(!allowlist.allows("https://staging.acme.com:9000/x"))
    }

    @Test("host comparison ignores case")
    func caseInsensitiveHost() {
        #expect(allowlist.allows("https://APP.ACME.COM/orders"))
    }

    @Test("non-https schemes are never allowed", arguments: [
        "file:///var/mobile/Containers/Data/Application/x.db",
        "javascript:alert(1)",
        "data:text/html,<script>alert(1)</script>",
        "about:blank",
    ])
    func nonHttpsRefused(url: String) {
        #expect(!allowlist.allows(url))
    }

    @Test("nil, empty, and unparseable input is refused rather than crashing")
    func malformedInput() {
        #expect(!allowlist.allows(nil))
        #expect(!allowlist.allows(""))
        #expect(!allowlist.allows("not a url at all"))
        #expect(!allowlist.allows("https://"))
    }

    /// A config that failed to load must not become an open door.
    @Test("an empty allowlist denies everything")
    func emptyDeniesAll() {
        let empty = OriginAllowlist([])

        #expect(empty.isEmpty)
        #expect(!empty.allows("https://app.acme.com/"))
    }

    @Test("malformed entries are dropped without discarding the good ones")
    func mixedEntries() {
        let mixed = OriginAllowlist(["http://insecure.example", "https://app.acme.com", "]["])

        #expect(mixed.origins == ["https://app.acme.com"])
        #expect(mixed.allows("https://app.acme.com/"))
    }
}

/// Replacing the base user agent breaks feature detection on the customer's own
/// site. These exist to stop anyone "tidying" that away.
struct UserAgentTests {
    private let safari = """
    Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 \
    (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1
    """

    @Test("the browser user agent is preserved in full")
    func basePreserved() {
        let built = UserAgent.build(base: safari, shellVersion: "1.0.0", suffix: nil)
        #expect(built.hasPrefix(safari))
    }

    @Test("the shell token is appended so a site can detect the app")
    func tokenAppended() {
        let built = UserAgent.build(base: safari, shellVersion: "1.4.0", suffix: nil)

        #expect(built.hasSuffix("Shellwright/1.4.0"))
        #expect(UserAgent.isShell(built))
    }

    @Test("a configured suffix is appended after the shell token")
    func suffixAppended() {
        let built = UserAgent.build(base: safari, shellVersion: "1.0.0", suffix: "AcmeApp/1.4.0")
        #expect(built == "\(safari) Shellwright/1.0.0 AcmeApp/1.4.0")
    }

    @Test("a blank suffix adds nothing")
    func blankSuffix() {
        let built = UserAgent.build(base: safari, shellVersion: "1.0.0", suffix: "   ")
        #expect(built == "\(safari) Shellwright/1.0.0")
    }

    /// WebKit composes the full agent itself and only takes the suffix, which
    /// makes replacing the base structurally impossible on iOS — a property the
    /// Android side has to achieve by discipline.
    @Test("applicationNameForUserAgent carries only the appended part")
    func applicationName() {
        #expect(UserAgent.applicationName(shellVersion: "1.0.0", suffix: nil) == "Shellwright/1.0.0")
        #expect(
            UserAgent.applicationName(shellVersion: "1.0.0", suffix: "AcmeApp/1.4.0")
                == "Shellwright/1.0.0 AcmeApp/1.4.0"
        )
    }

    @Test("an ordinary browser is not mistaken for the shell")
    func plainBrowser() {
        #expect(!UserAgent.isShell(safari))
    }
}

/// The offline page is bundled, never fetched, and themed at load time.
struct OfflinePageTests {
    private let template = """
    <html><body style="background: __BACKGROUND__; color: __FOREGROUND__">
    <h1>__TITLE__</h1><p>__BODY__</p>
    <button style="background: __ACCENT__">__RETRY__</button>
    </body></html>
    """

    private let strings = OfflinePage.Strings(
        title: "You are offline",
        body: "This page needs a connection.",
        retry: "Try again"
    )

    @Test("colours and copy are substituted")
    func rendersTheme() {
        let page = OfflinePage(template: template)
            .render(background: "#0B1220", foreground: "#E5E7EB", accent: "#2563EB", strings: strings)

        #expect(page.contains("#0B1220"))
        #expect(page.contains("You are offline"))
        #expect(!page.contains("__TITLE__"))
        #expect(!page.contains("__BACKGROUND__"))
    }

    /// The strings are ours, but they are localised — a translator should not
    /// be able to break the page, let alone inject into it.
    @Test("localized copy is escaped before it reaches the page")
    func escapesCopy() {
        let hostile = OfflinePage.Strings(
            title: "<script>alert(1)</script>",
            body: "Vous n'êtes pas connecté",
            retry: "Réessayer & continuer"
        )

        let page = OfflinePage(template: template).render(
            background: "#000", foreground: "#fff", accent: "#00f", strings: hostile
        )

        #expect(!page.contains("<script>"))
        #expect(page.contains("&lt;script&gt;"))
        #expect(page.contains("&#39;"))
        #expect(page.contains("&amp;"))
    }

    /// Loading it against the site's origin would give a bundled asset the
    /// site's cookies and storage.
    @Test("the page is loaded against about:blank, not the site's origin")
    func baseURLIsBlank() {
        #expect(OfflinePage.baseURL?.absoluteString == "about:blank")
    }
}
