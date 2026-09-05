package dev.shellwright.shell.routing

import dev.shellwright.shell.config.LinkRule

/**
 * Decides where every navigation goes.
 *
 * Correctness here is what makes the app feel coherent rather than like a
 * browser someone put a tab bar on. It runs on the UI thread on every
 * navigation, and a single-page app can fire it hundreds of times in a session,
 * so the budget is under 1 ms mean over 10,000 resolutions
 * (`TC-S02-PRF-004`).
 */
public class LinkRouter(
    rules: List<LinkRule>,
    private val downloadExtensions: Set<String> = DEFAULT_DOWNLOAD_EXTENSIONS,
    cacheSize: Int = DEFAULT_CACHE_SIZE,
) {
    /** A rule whose pattern compiled, paired with the action it selects. */
    private class Compiled(val regex: SafeRegex, val action: LinkAction)

    // Compiled once, at construction. Never per navigation.
    private val compiled: List<Compiled> = rules.mapNotNull { rule ->
        SafeRegex.compile(rule.pattern)?.let { Compiled(it, actionOf(rule.action)) }
    }

    /** Patterns that did not compile, so the shell can report them once. */
    public val rejectedPatterns: List<String> =
        rules.filter { SafeRegex.compile(it.pattern) == null }.map { it.pattern }

    private val cache = LruCache<String, LinkAction>(cacheSize)

    /**
     * Resolves where [url] should open.
     *
     * Non-http schemes are decided before any regex runs: `mailto:` belongs to
     * the mail app whatever the rules say, and evaluating two hundred patterns
     * against it would be wasted work.
     */
    public fun resolve(url: String): LinkAction {
        cache.get(url)?.let { return it }

        val resolved = resolveUncached(url)
        cache.put(url, resolved)
        return resolved
    }

    private fun resolveUncached(url: String): LinkAction {
        schemeAction(url)?.let { return it }
        if (isDownload(url)) return LinkAction.Download(url)

        // First match wins, in declared order. This is what makes the studio's
        // drag-to-reorder meaningful, so it must never become "best match".
        compiled.forEach { rule ->
            if (rule.regex.matches(url)) return rule.action
        }

        // Nothing matched. The studio warns about a missing catch-all
        // (CFG_LINK_RULE_NO_CATCHALL); the shell still needs a defined answer,
        // and sending an unrecognised link to the browser is the safe one.
        return LinkAction.ExternalBrowser
    }

    /** Actions decided by URL scheme alone, before any pattern is considered. */
    private fun schemeAction(url: String): LinkAction? {
        val scheme = url.substringBefore(':', missingDelimiterValue = "").lowercase()
        return when (scheme) {
            "http", "https" -> null
            "mailto", "tel", "sms", "intent", "geo", "market", "whatsapp" -> LinkAction.External(url)
            // A file: URL must never be honoured. See OriginAllowlist and the
            // WebView hardening in ShellWebViewClient.
            "file", "content", "javascript", "data" -> LinkAction.Block
            "" -> null
            else -> LinkAction.External(url)
        }
    }

    private fun isDownload(url: String): Boolean {
        val path = url.substringBefore('?').substringBefore('#')
        val extension = path.substringAfterLast('.', missingDelimiterValue = "").lowercase()
        return extension.isNotEmpty() && extension in downloadExtensions
    }

    private fun actionOf(action: String): LinkAction = when (action) {
        "internal" -> LinkAction.Internal
        "modal" -> LinkAction.Modal
        "readerModal" -> LinkAction.ReaderModal
        "block" -> LinkAction.Block
        // The schema constrains this to a known set, and a shell at version N
        // may see an action added at version N+1. Treating an unknown action as
        // "open in the browser" degrades gracefully; crashing does not.
        else -> LinkAction.ExternalBrowser
    }

    public companion object {
        private const val DEFAULT_CACHE_SIZE = 256

        private val DEFAULT_DOWNLOAD_EXTENSIONS = setOf(
            "pdf", "zip", "csv", "xlsx", "xls", "docx", "doc", "pptx", "ppt",
            "apk", "dmg", "exe", "mp3", "mp4", "mov", "epub",
        )
    }
}

/**
 * A minimal insertion-ordered LRU.
 *
 * `android.util.LruCache` would do, but it is unavailable to JVM unit tests and
 * the router's tests are the ones that matter most here.
 */
internal class LruCache<K : Any, V : Any>(private val maxSize: Int) {
    private val entries = object : LinkedHashMap<K, V>(16, 0.75f, true) {
        override fun removeEldestEntry(eldest: MutableMap.MutableEntry<K, V>?): Boolean =
            size > maxSize
    }

    fun get(key: K): V? = entries[key]

    fun put(key: K, value: V) {
        entries[key] = value
    }

    val size: Int get() = entries.size
}
