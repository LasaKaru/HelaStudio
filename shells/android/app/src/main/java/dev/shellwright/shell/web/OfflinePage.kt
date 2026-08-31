package dev.shellwright.shell.web

import android.content.Context
import androidx.annotation.ColorInt
import dev.shellwright.shell.R

/**
 * The page shown when the network, not the site, has failed.
 *
 * ⚠️ Bundled as an asset and never fetched. An offline page loaded over the
 * network is a contradiction, and it is a mistake apps in this category
 * genuinely make.
 *
 * Themed at load time rather than at build time, so a colour change stays a
 * content-key change rather than forcing a recompile (ADR 0004).
 */
public class OfflinePage(private val context: Context) {

    /**
     * Renders the page with the app's colours and the user's language.
     *
     * @param background the splash background, so offline looks like the app
     * @param foreground readable text against [background]
     * @param accent the retry button, in the app's primary colour
     */
    public fun render(
        @ColorInt background: Int,
        @ColorInt foreground: Int,
        @ColorInt accent: Int,
    ): String {
        val template = context.assets.open(ASSET_NAME)
            .bufferedReader()
            .use { it.readText() }

        return template
            .replace("__BACKGROUND__", hex(background))
            .replace("__FOREGROUND__", hex(foreground))
            .replace("__ACCENT__", hex(accent))
            // Localised, because an error message in the wrong language is
            // worse than no message.
            .replace("__TITLE__", context.getString(R.string.offline_title))
            .replace("__BODY__", context.getString(R.string.offline_body))
            .replace("__RETRY__", context.getString(R.string.offline_retry))
    }

    /**
     * The base URL the page is loaded against.
     *
     * ⚠️ `about:blank`, deliberately. Loading it against the site's origin would
     * give a bundled asset the site's cookies and storage; loading it from a
     * `file://` URL would need file access enabled, which is exactly what
     * [ShellWebViewFactory] switches off.
     */
    public val baseUrl: String = "about:blank"

    private fun hex(@ColorInt color: Int): String =
        String.format(java.util.Locale.ROOT, "#%06X", RGB_MASK and color)

    private companion object {
        const val ASSET_NAME = "offline.html"
        const val RGB_MASK = 0xFFFFFF
    }
}
