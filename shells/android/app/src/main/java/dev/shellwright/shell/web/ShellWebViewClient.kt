package dev.shellwright.shell.web

import android.graphics.Bitmap
import android.os.Build
import android.webkit.RenderProcessGoneDetail
import android.webkit.WebResourceError
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import dev.shellwright.shell.routing.LinkAction
import dev.shellwright.shell.routing.LinkRouter

/**
 * Routes navigations and handles failures.
 *
 * Two things here are easy to get wrong and embarrassing when you do:
 *
 * 1. **Only network-level failures show the offline page.** A 404 from the
 *    customer's own site must render *their* 404, not ours. Replacing a
 *    customer's carefully designed error page with a generic one is a common
 *    complaint about apps in this category.
 * 2. **The renderer process can die on its own**, taking the whole app with it
 *    unless [onRenderProcessGone] returns true. On low-memory devices this is
 *    not rare.
 */
public class ShellWebViewClient(
    private val router: LinkRouter,
    private val allowlist: OriginAllowlist,
    private val callbacks: Callbacks,
) : WebViewClient() {

    /** What the host activity needs to know about. */
    public interface Callbacks {
        /** Open [url] outside the current web view. */
        public fun onExternalNavigation(action: LinkAction, url: String)

        /** A main-frame load started. */
        public fun onPageLoading(url: String)

        /** A main-frame load finished. */
        public fun onPageFinished(url: String, canGoBack: Boolean)

        /** The network, not the site, failed. Show the offline page. */
        public fun onNetworkFailure(failedUrl: String)

        /** The renderer died and the web view must be rebuilt. */
        public fun onRendererGone(wasCrash: Boolean)
    }

    override fun shouldOverrideUrlLoading(
        view: WebView,
        request: WebResourceRequest,
    ): Boolean {
        val url = request.url.toString()

        return when (val action = router.resolve(url)) {
            // Let the web view proceed only for a URL that is genuinely ours.
            is LinkAction.Internal -> {
                if (allowlist.allows(url)) {
                    false
                } else {
                    // A rule said "internal" but the origin is not allowed.
                    // The allowlist wins: it is the security boundary, and the
                    // validator warns about this at config time
                    // (CFG_ORIGIN_NOT_COVERED).
                    callbacks.onExternalNavigation(LinkAction.ExternalBrowser, url)
                    true
                }
            }

            is LinkAction.Block -> true

            else -> {
                callbacks.onExternalNavigation(action, url)
                true
            }
        }
    }

    override fun onPageStarted(view: WebView, url: String, favicon: Bitmap?) {
        callbacks.onPageLoading(url)
    }

    override fun onPageFinished(view: WebView, url: String) {
        callbacks.onPageFinished(url, view.canGoBack())
    }

    override fun onReceivedError(
        view: WebView,
        request: WebResourceRequest,
        error: WebResourceError,
    ) {
        // Sub-resource failures are the page's business, not ours.
        if (!request.isForMainFrame) return

        if (isNetworkLevel(error.errorCode)) {
            callbacks.onNetworkFailure(request.url.toString())
        }
    }

    override fun onReceivedHttpError(
        view: WebView,
        request: WebResourceRequest,
        errorResponse: WebResourceResponse,
    ) {
        // ⚠️ Deliberately does nothing. An HTTP status means the server
        // answered, so the site's own error page should render.
    }

    override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail?): Boolean {
        // ⚠️ didCrash() is API 26; minSdk is 24. On 24 and 25 the reason is
        // simply unavailable, and recovery does not depend on knowing it.
        val wasCrash = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && detail?.didCrash() == true

        callbacks.onRendererGone(wasCrash)
        // Returning true is what stops the whole app being killed with it.
        return true
    }

    /** Whether an error code means the network failed rather than the site. */
    private fun isNetworkLevel(errorCode: Int): Boolean = errorCode in NETWORK_ERRORS

    private companion object {
        /**
         * Only failures where nothing was reached at all.
         *
         * Notably absent: ERROR_FILE_NOT_FOUND and anything that implies a
         * server responded.
         */
        val NETWORK_ERRORS = setOf(
            ERROR_HOST_LOOKUP,
            ERROR_CONNECT,
            ERROR_TIMEOUT,
            ERROR_IO,
            ERROR_PROXY_AUTHENTICATION,
            ERROR_UNSUPPORTED_SCHEME,
            ERROR_FAILED_SSL_HANDSHAKE,
        )
    }
}
