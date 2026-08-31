package dev.shellwright.shell.web

import android.net.Uri
import android.webkit.PermissionRequest
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebView
import dev.shellwright.shell.config.Permissions

/**
 * Handles the parts of the page that need the app's cooperation.
 *
 * The file chooser is the one that matters most: without [onShowFileChooser]
 * every `<input type="file">` on the customer's site silently does nothing,
 * which is the single most common "the app is broken" report in this category.
 */
public class ShellWebChromeClient(
    private val permissions: Permissions,
    private val callbacks: Callbacks,
) : WebChromeClient() {

    /** What the host activity has to do on the page's behalf. */
    public interface Callbacks {
        /** The page's `<title>` changed. */
        public fun onTitleChanged(title: String)

        /** Main-frame load progress, 0 to 100. */
        public fun onProgress(percent: Int)

        /**
         * Show a file picker.
         *
         * @return false if no picker could be shown, so the page is told the
         *   selection was cancelled rather than left waiting forever.
         */
        public fun onFileChooserRequested(
            callback: ValueCallback<Array<Uri>>,
            params: FileChooserParams,
        ): Boolean
    }

    override fun onReceivedTitle(view: WebView, title: String?) {
        title?.takeIf { it.isNotBlank() }?.let(callbacks::onTitleChanged)
    }

    override fun onProgressChanged(view: WebView, newProgress: Int) {
        callbacks.onProgress(newProgress)
    }

    override fun onShowFileChooser(
        webView: WebView,
        filePathCallback: ValueCallback<Array<Uri>>,
        fileChooserParams: FileChooserParams,
    ): Boolean {
        val shown = callbacks.onFileChooserRequested(filePathCallback, fileChooserParams)
        if (!shown) {
            // ⚠️ The page waits indefinitely unless it is told. A null result
            // means "cancelled", which is what actually happened.
            filePathCallback.onReceiveValue(null)
        }
        return true
    }

    /**
     * Grants only what the configuration declared.
     *
     * ⚠️ The page asks; the config decides. A site that requests the camera in
     * an app whose config never enabled it is denied, because the app has no
     * Android permission to give and the store listing makes no such claim.
     */
    override fun onPermissionRequest(request: PermissionRequest) {
        val granted = request.resources.filter(::isConfigured).toTypedArray()

        if (granted.isEmpty()) {
            request.deny()
        } else {
            request.grant(granted)
        }
    }

    private fun isConfigured(resource: String): Boolean = when (resource) {
        PermissionRequest.RESOURCE_VIDEO_CAPTURE -> permissions.camera
        PermissionRequest.RESOURCE_AUDIO_CAPTURE -> permissions.microphone
        // Anything not modelled is denied. Defaulting to deny is the only safe
        // direction for a capability the config never mentioned.
        else -> false
    }
}
