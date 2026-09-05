package dev.shellwright.shell.web

import android.annotation.SuppressLint
import android.content.Context
import android.webkit.CookieManager
import android.webkit.WebSettings
import android.webkit.WebView
import dev.shellwright.shell.BuildConfig
import dev.shellwright.shell.config.ShellConfig

/**
 * Builds and hardens the WebView.
 *
 * Most of the value in this file is in the settings that are switched *off*.
 * A WebView with JavaScript enabled and file access allowed can read the app's
 * own private storage — that is the shape of a real breach in this category,
 * not a theoretical one.
 */
public object ShellWebViewFactory {

    /**
     * Creates a configured WebView.
     *
     * @param context an Activity context; a WebView must never be built from
     *   the application context or it cannot show dialogs or the file chooser.
     */
    @SuppressLint("SetJavaScriptEnabled")
    public fun create(context: Context, config: ShellConfig): WebView {
        val webView = WebView(context)

        webView.settings.apply {
            // The product is a web app in a native shell; without this there is
            // no product. Everything below is what makes it safe.
            javaScriptEnabled = true

            domStorageEnabled = true
            loadWithOverviewMode = true
            useWideViewPort = true
            mediaPlaybackRequiresUserGesture = false

            builtInZoomControls = config.webOverrides.allowZoom
            displayZoomControls = false

            // ⚠️ A file-scheme page with JavaScript enabled can read the app's
            // private data directory. There is no legitimate reason for a
            // hosted site to need any of these.
            allowFileAccess = false
            allowContentAccess = false
            @Suppress("DEPRECATION")
            allowFileAccessFromFileURLs = false
            @Suppress("DEPRECATION")
            allowUniversalAccessFromFileURLs = false

            // ⚠️ An https page must not be able to pull in http sub-resources.
            mixedContentMode = WebSettings.MIXED_CONTENT_NEVER_ALLOW

            // ⚠️ Append, never replace. Replacing the base string breaks
            // feature detection on the customer's own site — see UserAgent.
            userAgentString = UserAgent.build(
                base = userAgentString,
                shellVersion = BuildConfig.VERSION_NAME,
                suffix = config.webOverrides.userAgentSuffix,
            )

            cacheMode = WebSettings.LOAD_DEFAULT
            setGeolocationEnabled(config.permissions.wantsLocation)

            // Respect the user's system text size. Ignoring it is an
            // accessibility failure that a store reviewer may well notice.
            textZoom = DEFAULT_TEXT_ZOOM
        }

        webView.isVerticalScrollBarEnabled = true
        webView.isHorizontalScrollBarEnabled = false
        webView.overScrollMode = WebView.OVER_SCROLL_IF_CONTENT_SCROLLS

        configureCookies(webView, config)
        return webView
    }

    /**
     * Cookie policy.
     *
     * Persisting cookies is what keeps a user signed in between launches.
     * Turning it off signs them out on every cold start, which reads as a bug
     * even when it was asked for.
     */
    private fun configureCookies(webView: WebView, config: ShellConfig) {
        val cookies = CookieManager.getInstance()
        cookies.setAcceptCookie(config.webOverrides.persistCookies)
        cookies.setAcceptThirdPartyCookies(webView, config.webOverrides.persistCookies)
    }

    /** 100% means "follow the system text size", which is what we want. */
    private const val DEFAULT_TEXT_ZOOM = 100
}
