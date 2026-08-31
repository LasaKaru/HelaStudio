package dev.shellwright.shell

import android.net.Uri
import android.os.Bundle
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.getValue
import androidx.core.net.toUri
import dev.shellwright.shell.ui.ShellScaffold
import dev.shellwright.shell.ui.ShellTheme

/**
 * The single activity that hosts everything.
 *
 * Startup order matters more here than anywhere else in the codebase, and it is
 * the reverse of the obvious one:
 *
 * 1. Read only the first-frame values from the config, on the main thread, in
 *    under five milliseconds ([dev.shellwright.shell.config.FastConfigReader]).
 * 2. Draw the native skeleton — bars, colours, tab labels — and dismiss the
 *    splash. The app is now visibly an app.
 * 3. Parse the full config on a background dispatcher, build the web view, and
 *    start loading.
 *
 * Doing step 3 before step 2 is the difference between an app that appears
 * instantly and one that shows a white rectangle for half a second.
 */
public class ShellActivity : ComponentActivity() {

    private val viewModel: ShellViewModel by viewModels { ShellViewModel.factory(this) }

    private var pendingFileChooser: ValueCallback<Array<Uri>>? = null

    private val fileChooserLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult(),
    ) { result ->
        val callback = pendingFileChooser
        pendingFileChooser = null
        callback?.onReceiveValue(
            WebChromeClient.FileChooserParams.parseResult(result.resultCode, result.data),
        )
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        val splash = installSplashScreen()

        super.onCreate(savedInstanceState)

        // Phase one. Cheap enough to block on; everything drawn below needs it.
        val firstFrame = viewModel.firstFrame

        // Hold the splash only until the native skeleton exists — not until the
        // web content arrives, which would leave a gap between the two.
        splash.setKeepOnScreenCondition { !viewModel.skeletonDrawn }

        setContent {
            val state by viewModel.uiState.collectAsStateWithLifecycle()
            val webView by viewModel.webView.collectAsStateWithLifecycle()

            ShellTheme(
                colors = viewModel.colors,
                useDarkTheme = viewModel.useDarkTheme(isSystemInDarkTheme()),
            ) {
                ShellScaffold(
                    state = state,
                    colors = viewModel.colors,
                    webView = webView,
                    onTabSelected = viewModel::selectTab,
                    onActionSelected = { action -> onAction(action.type) },
                    onBack = ::onBackRequested,
                )
            }

            viewModel.onSkeletonDrawn(firstFrame)
        }

        onBackPressedDispatcher.addCallback(this) { onBackRequested() }
    }

    /**
     * Back, in the order a user expects.
     *
     * Web history first, then the tab root, then leave the app. Exiting from a
     * page three levels deep would lose the user's place, which is the most
     * common complaint about hybrid apps that get this wrong.
     */
    private fun onBackRequested() {
        val webView = viewModel.webView.value

        when {
            webView?.canGoBack() == true -> webView.goBack()
            viewModel.canReturnToTabRoot() -> viewModel.returnToTabRoot()
            else -> finish()
        }
    }

    private fun onAction(type: String) {
        when (type) {
            "refresh" -> viewModel.webView.value?.reload()
            "share" -> shareCurrentPage()
            else -> viewModel.onCustomAction(type)
        }
    }

    private fun shareCurrentPage() {
        val url = viewModel.webView.value?.url ?: return
        val intent = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
            this.type = "text/plain"
            putExtra(android.content.Intent.EXTRA_TEXT, url)
        }
        startActivity(android.content.Intent.createChooser(intent, null))
    }

    /** Opens the system file picker on the page's behalf. */
    internal fun showFileChooser(
        callback: ValueCallback<Array<Uri>>,
        params: WebChromeClient.FileChooserParams,
    ): Boolean = runCatching {
        pendingFileChooser?.onReceiveValue(null)
        pendingFileChooser = callback
        fileChooserLauncher.launch(params.createIntent())
        true
    }.getOrElse {
        pendingFileChooser = null
        false
    }

    /** Opens [url] outside the app, keeping the user's colour scheme. */
    internal fun openExternally(url: String) {
        runCatching {
            androidx.browser.customtabs.CustomTabsIntent.Builder()
                .setShowTitle(true)
                .build()
                .launchUrl(this, url.toUri())
        }.onFailure {
            // Custom Tabs needs a compatible browser; not every device has one.
            runCatching {
                startActivity(android.content.Intent(android.content.Intent.ACTION_VIEW, url.toUri()))
            }
        }
    }

    override fun onDestroy() {
        // ⚠️ A retained WebView leaks its Activity. Destroy it here, after it
        // has been removed from the hierarchy.
        viewModel.destroyWebView()
        super.onDestroy()
    }
}

/** Registers a back callback without the androidx-activity-ktx dependency. */
private fun androidx.activity.OnBackPressedDispatcher.addCallback(
    owner: androidx.lifecycle.LifecycleOwner,
    handler: () -> Unit,
) {
    addCallback(
        owner,
        object : androidx.activity.OnBackPressedCallback(true) {
            override fun handleOnBackPressed() = handler()
        },
    )
}
