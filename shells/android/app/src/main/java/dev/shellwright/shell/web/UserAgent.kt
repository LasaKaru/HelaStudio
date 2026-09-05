package dev.shellwright.shell.web

/**
 * Builds the WebView's user agent.
 *
 * ⚠️ **Append, never replace.** Replacing the whole string breaks feature
 * detection on the customer's own site — their analytics stop recognising the
 * browser, their polyfill decisions go wrong, and their CDN may serve a desktop
 * layout. It is one of the highest-volume support tickets in this category, and
 * it is entirely avoidable.
 *
 * The appended token is also how a site detects it is running inside the app,
 * which is the documented mechanism behind `median.isApp()` and its equivalents.
 */
public object UserAgent {

    /** The token every Shellwright app carries, whatever else is appended. */
    public const val SHELL_TOKEN: String = "Shellwright"

    /**
     * Returns [base] with the shell token and any configured suffix appended.
     *
     * @param base the WebView's own user agent, never modified in place
     * @param shellVersion the shell template version, so a site can branch on it
     * @param suffix the customer's `webOverrides.userAgentSuffix`, if any
     */
    public fun build(base: String, shellVersion: String, suffix: String?): String {
        val parts = mutableListOf(base.trim(), "$SHELL_TOKEN/$shellVersion")
        suffix?.trim()?.takeIf { it.isNotEmpty() }?.let(parts::add)
        return parts.joinToString(separator = " ")
    }

    /** Whether a user agent string came from a Shellwright app. */
    public fun isShell(userAgent: String): Boolean = userAgent.contains("$SHELL_TOKEN/")
}
