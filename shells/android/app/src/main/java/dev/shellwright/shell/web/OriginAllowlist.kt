package dev.shellwright.shell.web

import java.net.URI
import java.net.URISyntaxException
import java.util.Locale

/**
 * The set of origins treated as the app itself.
 *
 * ⚠️ This is a security boundary, not a convenience. When the JavaScript bridge
 * lands in Sprint 09, a page outside this allowlist must have no bridge object
 * at all — not a bridge that refuses calls, no object
 * (`01_ENGINEERING_STANDARDS.md` §6.2). Enforcement is native, never in JS,
 * because JS running on the page is the thing being defended against.
 *
 * Built in Sprint 02, before there is anything privileged to gate, so that
 * gating is never something bolted onto a surface that already exists.
 *
 * Uses [URI] rather than `android.net.Uri` deliberately: this is the class most
 * worth unit-testing in the shell, and `android.net.Uri` is stubbed out in JVM
 * unit tests, which would leave it verified only on a device.
 */
public class OriginAllowlist(origins: List<String>) {

    private val allowed: Set<String> = origins.mapNotNull(::normalize).toSet()

    /** Whether [url] is on an origin the app considers its own. */
    public fun allows(url: String?): Boolean {
        if (url.isNullOrEmpty()) return false
        return normalize(url) in allowed
    }

    /** The normalised origins, for logging and diagnostics. */
    public val origins: Set<String> get() = allowed

    /** Whether any origin was configured at all. */
    public val isEmpty: Boolean get() = allowed.isEmpty()

    /**
     * Reduces a URL to `scheme://host[:port]`, or null if it is not an origin
     * this app may ever trust.
     */
    private fun normalize(value: String): String? {
        val uri = try {
            URI(value.trim())
        } catch (_: URISyntaxException) {
            return null
        }

        // ⚠️ https only. An http origin would let anyone on the network inject
        // a page that then counts as the app's own.
        val scheme = uri.scheme?.lowercase(Locale.ROOT)
        if (scheme != "https") return null

        val host = uri.host?.lowercase(Locale.ROOT)?.takeIf { it.isNotEmpty() } ?: return null
        val port = uri.port

        return if (port == -1) "https://$host" else "https://$host:$port"
    }
}
