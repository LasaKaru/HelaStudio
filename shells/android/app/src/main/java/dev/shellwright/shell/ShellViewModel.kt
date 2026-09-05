package dev.shellwright.shell

import android.webkit.WebView
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import androidx.compose.ui.graphics.Color
import dev.shellwright.shell.config.ConfigRepository
import dev.shellwright.shell.config.FastConfigReader
import dev.shellwright.shell.config.ShellConfig
import dev.shellwright.shell.net.ConnectivityObserver
import dev.shellwright.shell.net.NetworkState
import dev.shellwright.shell.routing.LinkAction
import dev.shellwright.shell.routing.LinkRouter
import dev.shellwright.shell.ui.ActionUi
import dev.shellwright.shell.ui.ShellColors
import dev.shellwright.shell.ui.ShellIcon
import dev.shellwright.shell.ui.ShellStrings
import dev.shellwright.shell.ui.ShellUiState
import dev.shellwright.shell.ui.TabUi
import androidx.compose.ui.graphics.toArgb
import dev.shellwright.shell.web.OfflinePage
import dev.shellwright.shell.web.OriginAllowlist
import dev.shellwright.shell.web.ShellWebChromeClient
import dev.shellwright.shell.web.ShellWebViewClient
import dev.shellwright.shell.web.ShellWebViewFactory
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/**
 * Holds everything that must survive a rotation, and sequences startup.
 *
 * The web view lives here rather than in the composition because recreating it
 * on every configuration change would reload the page and lose the user's
 * scroll position and form state.
 */
public class ShellViewModel(
    private val repository: ConfigRepository,
    private val connectivity: ConnectivityObserver,
    private val host: ShellHost,
    private val strings: ShellStrings,
    private val offlinePage: OfflinePage,
) : ViewModel() {

    /** What the view model needs from the Activity it cannot do itself. */
    public interface ShellHost {
        /** Open a URL outside the current web view. */
        public fun openExternally(url: String)

        /** Build a web view; only an Activity context may do this. */
        public fun createWebView(config: ShellConfig): WebView

        /**
         * Show the system file picker on the page's behalf.
         *
         * @return false when no picker could be shown, so the page is told the
         *   selection was cancelled rather than left waiting forever.
         */
        public fun showFileChooser(
            callback: android.webkit.ValueCallback<Array<android.net.Uri>>,
            params: android.webkit.WebChromeClient.FileChooserParams,
        ): Boolean
    }

    /** Phase one of the config parse. Read on the main thread, before drawing. */
    public val firstFrame: FastConfigReader.FirstFrame = repository.readFirstFrame()

    /** Whether the native skeleton has been drawn, so the splash may go. */
    public var skeletonDrawn: Boolean = false
        private set

    /** The colours the chrome paints with, available before the full parse. */
    public val colors: ShellColors = ShellColors(
        primary = ShellColors.parseColor(firstFrame.themePrimary, DEFAULT_PRIMARY),
        navBar = ShellColors.parseColor(firstFrame.themeNavBar, Color.White),
        tabBar = ShellColors.parseColor(firstFrame.themeTabBar, Color.White),
        splashBackground = ShellColors.parseColor(firstFrame.splashBackground, Color.White),
    )

    private val _uiState = MutableStateFlow(initialState())
    /** What the chrome should currently show. */
    public val uiState: StateFlow<ShellUiState> = _uiState.asStateFlow()

    private val _webView = MutableStateFlow<WebView?>(null)
    /** The web view, once phase two has produced a config to configure it with. */
    public val webView: StateFlow<WebView?> = _webView.asStateFlow()

    private var config: ShellConfig? = null
    private var router: LinkRouter? = null
    private var allowlist: OriginAllowlist = OriginAllowlist(emptyList())
    private var networkState: NetworkState = NetworkState.Online
    private var lastFailedUrl: String? = null

    /**
     * Phase one is on screen; start phase two.
     *
     * Called from the composition rather than `onCreate` so that the full parse
     * genuinely begins after the first frame, not merely on another thread.
     */
    public fun onSkeletonDrawn(frame: FastConfigReader.FirstFrame) {
        if (skeletonDrawn) return
        skeletonDrawn = true

        viewModelScope.launch {
            repository.load()
                .onSuccess { onConfigLoaded(it, frame) }
                .onFailure { onConfigFailed() }
        }

        viewModelScope.launch {
            connectivity.observe().collect { state ->
                val recovered = networkState == NetworkState.Offline && state == NetworkState.Online
                networkState = state
                if (recovered) retryFailedLoad()
            }
        }
    }

    private fun onConfigLoaded(loaded: ShellConfig, frame: FastConfigReader.FirstFrame) {
        config = loaded
        router = LinkRouter(loaded.linkRules)
        allowlist = OriginAllowlist(loaded.app.allowedOrigins)

        _uiState.update { current ->
            current.copy(
                title = titleFor(loaded, frame.appName),
                topBarEnabled = loaded.navigation.topBar.enabled,
                tabs = tabsOf(loaded),
                selectedTabId = tabsOf(loaded).firstOrNull()?.id,
                actions = actionsOf(loaded),
            )
        }

        val created = host.createWebView(loaded)
        attachClients(created, loaded)
        _webView.value = created
        created.loadUrl(loaded.app.initialUrl)
    }

    /**
     * Phase two failed.
     *
     * The skeleton is already on screen, so the app does not disappear. It
     * cannot load anything without a config, so it says so rather than showing
     * an empty frame forever.
     */
    private fun onConfigFailed() {
        _uiState.update { it.copy(title = firstFrame.appName, progress = 0) }
    }

    /** Whether the app should render dark, given the device's current setting. */
    public fun useDarkTheme(systemInDark: Boolean): Boolean =
        when (config?.branding?.darkMode) {
            "light" -> false
            "dark" -> true
            else -> systemInDark
        }

    /** Navigate to a tab's destination. */
    public fun selectTab(tab: TabUi) {
        val view = _webView.value ?: return
        val target = absoluteUrl(tab.url)

        // Tapping the already-selected tab scrolls to top rather than
        // reloading. Small, and users notice.
        if (_uiState.value.selectedTabId == tab.id && view.url == target) {
            view.scrollTo(0, 0)
            return
        }

        _uiState.update { it.copy(selectedTabId = tab.id) }
        view.loadUrl(target)
    }

    /** Whether back should return to the current tab's root before exiting. */
    public fun canReturnToTabRoot(): Boolean {
        val tabs = _uiState.value.tabs
        if (tabs.isEmpty()) return false

        val root = tabs.firstOrNull()?.url?.let(::absoluteUrl)
        return root != null && _webView.value?.url != root
    }

    /** Return to the first tab. */
    public fun returnToTabRoot() {
        _uiState.value.tabs.firstOrNull()?.let(::selectTab)
    }

    /** A top bar action the shell has no built-in behaviour for. */
    public fun onCustomAction(type: String) {
        // Custom actions call back into the page. Until the bridge lands in
        // Sprint 09 there is nothing to call, so this is deliberately inert
        // rather than pretending to work.
        check(type.isNotEmpty()) { "A custom action must have a type." }
    }

    /** Routes a navigation the web view refused to handle itself. */
    public fun onExternalNavigation(action: LinkAction, url: String) {
        when (action) {
            is LinkAction.External -> host.openExternally(action.uri)
            is LinkAction.Download -> host.openExternally(action.url)
            is LinkAction.Block -> Unit
            else -> host.openExternally(url)
        }
    }

    /** The page's title changed. */
    public fun onTitleChanged(title: String) {
        if (config?.navigation?.topBar?.titleSource != "documentTitle") return
        _uiState.update { it.copy(title = title) }
    }

    /** Main-frame load progress. */
    public fun onProgress(percent: Int) {
        _uiState.update { it.copy(progress = percent) }
    }

    /** A main-frame load finished. */
    public fun onPageFinished(url: String, canGoBack: Boolean) {
        lastFailedUrl = null
        _uiState.update { state ->
            state.copy(
                canGoBack = canGoBack,
                progress = 0,
                selectedTabId = matchingTab(url) ?: state.selectedTabId,
            )
        }
    }

    /**
     * The network failed, so show the offline page and remember what to retry.
     *
     * Only network-level failures reach here. An HTTP status means the server
     * answered, so the site's own error page renders instead — see
     * [ShellWebViewClient.onReceivedHttpError].
     */
    public fun onNetworkFailure(failedUrl: String) {
        lastFailedUrl = failedUrl

        if (config?.offline?.enabled != true) return

        _webView.value?.loadDataWithBaseURL(
            offlinePage.baseUrl,
            offlinePage.render(
                background = colors.splashBackground.toArgb(),
                foreground = if (colors.navBarIsLight) FOREGROUND_ON_LIGHT else FOREGROUND_ON_DARK,
                accent = colors.primary.toArgb(),
            ),
            "text/html",
            "utf-8",
            null,
        )
    }

    /**
     * Reload what failed, now that the connection is back.
     *
     * Reloads the *failed URL*, not the offline page: reloading the page the
     * user is looking at would just render the offline page again.
     */
    private fun retryFailedLoad() {
        val url = lastFailedUrl ?: return
        lastFailedUrl = null
        _webView.value?.loadUrl(url)
    }

    /** Rebuilds the web view after its renderer process died. */
    public fun onRendererGone() {
        val loaded = config ?: return
        val url = _webView.value?.url ?: loaded.app.initialUrl

        destroyWebView()

        val replacement = host.createWebView(loaded)
        attachClients(replacement, loaded)
        _webView.value = replacement
        replacement.loadUrl(url)
    }

    /**
     * Attaches the clients that make a web view a shell rather than a browser.
     *
     * Kept in one place so that the renderer-recovery path in [onRendererGone]
     * cannot drift from the initial assembly — a rebuilt web view missing its
     * link router would silently start opening every link in the browser.
     */
    private fun attachClients(view: WebView, loaded: ShellConfig) {
        val linkRouter = router ?: LinkRouter(loaded.linkRules)

        view.webViewClient = ShellWebViewClient(
            router = linkRouter,
            allowlist = allowlist,
            callbacks = object : ShellWebViewClient.Callbacks {
                override fun onExternalNavigation(action: LinkAction, url: String) {
                    this@ShellViewModel.onExternalNavigation(action, url)
                }

                override fun onPageLoading(url: String) {
                    onProgress(1)
                }

                override fun onPageFinished(url: String, canGoBack: Boolean) {
                    this@ShellViewModel.onPageFinished(url, canGoBack)
                }

                override fun onNetworkFailure(failedUrl: String) {
                    this@ShellViewModel.onNetworkFailure(failedUrl)
                }

                override fun onRendererGone(wasCrash: Boolean) {
                    this@ShellViewModel.onRendererGone()
                }
            },
        )

        view.webChromeClient = ShellWebChromeClient(
            permissions = loaded.permissions,
            callbacks = object : ShellWebChromeClient.Callbacks {
                override fun onTitleChanged(title: String) {
                    this@ShellViewModel.onTitleChanged(title)
                }

                override fun onProgress(percent: Int) {
                    this@ShellViewModel.onProgress(percent)
                }

                override fun onFileChooserRequested(
                    callback: android.webkit.ValueCallback<Array<android.net.Uri>>,
                    params: android.webkit.WebChromeClient.FileChooserParams,
                ): Boolean = host.showFileChooser(callback, params)
            },
        )
    }

    /** Releases the web view. Must be called before the Activity is destroyed. */
    public fun destroyWebView() {
        _webView.value?.let { view ->
            (view.parent as? android.view.ViewGroup)?.removeView(view)
            view.destroy()
        }
        _webView.value = null
    }

    /** Which tab, if any, the current URL belongs to. */
    private fun matchingTab(url: String): String? {
        val loaded = config ?: return null
        return loaded.navigation.tabBar.items.firstOrNull { item ->
            item.activePattern?.let { pattern ->
                runCatching { Regex(pattern).containsMatchIn(url) }.getOrDefault(false)
            } == true
        }?.id
    }

    /** Resolves a config URL, which may be a path, against the start URL. */
    private fun absoluteUrl(url: String): String {
        if (!url.startsWith("/")) return url
        val start = config?.app?.initialUrl ?: firstFrame.initialUrl
        return runCatching { java.net.URI(start).resolve(url).toString() }.getOrDefault(url)
    }

    private fun titleFor(loaded: ShellConfig, fallback: String): String =
        when (loaded.navigation.topBar.titleSource) {
            "static" -> loaded.navigation.topBar.staticTitle ?: loaded.app.name
            "none" -> ""
            else -> fallback.ifEmpty { loaded.app.name }
        }

    private fun tabsOf(loaded: ShellConfig): List<TabUi> {
        if (!loaded.navigation.tabBar.enabled) return emptyList()

        return loaded.navigation.tabBar.items.map { item ->
            TabUi(
                id = item.id,
                label = item.label.resolve(java.util.Locale.getDefault().toLanguageTag()),
                icon = ShellIcon.forName(item.icon),
                url = item.url,
            )
        }
    }

    /**
     * Top bar actions, with their accessibility text resolved.
     *
     * An action's label is what a screen reader announces, so the fallback is
     * localized copy rather than the raw config `type` — "Menu", not "custom".
     */
    private fun actionsOf(loaded: ShellConfig): List<ActionUi> =
        loaded.navigation.topBar.actions.map { action ->
            ActionUi(
                id = action.id,
                type = action.type,
                label = action.label?.resolve(java.util.Locale.getDefault().toLanguageTag())
                    ?: strings.defaultActionLabel(action.type),
            )
        }

    /** The skeleton state, drawn from phase one alone. */
    private fun initialState() = ShellUiState(
        title = firstFrame.appName,
        topBarEnabled = firstFrame.topBarEnabled,
        // Phase one knows the labels but not the URLs, so the bar is drawn with
        // real text and becomes tappable once phase two lands.
        tabs = if (firstFrame.tabBarEnabled) {
            firstFrame.tabLabels.mapIndexed { index, label ->
                TabUi(id = "skeleton-$index", label = label, icon = ShellIcon.Fallback, url = "")
            }
        } else {
            emptyList()
        },
        selectedTabId = null,
        actions = emptyList(),
        progress = 0,
        canGoBack = false,
    )

    public companion object {
        private val DEFAULT_PRIMARY = Color(0xFF2563EB)

        /** Near-black and near-white, so the offline text stays readable. */
        private const val FOREGROUND_ON_LIGHT = 0xFF111827.toInt()
        private const val FOREGROUND_ON_DARK = 0xFFE5E7EB.toInt()

        /** Builds the view model for an Activity. */
        public fun factory(activity: ShellActivity): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    val viewModel = ShellViewModel(
                        repository = ConfigRepository(activity.applicationContext),
                        connectivity = ConnectivityObserver(activity.applicationContext),
                        host = ShellHostImpl(activity),
                        // Resolved eagerly so the view model holds text, not a
                        // Context it would outlive.
                        strings = ShellStrings.from(activity),
                        offlinePage = OfflinePage(activity.applicationContext),
                    )
                    return viewModel as T
                }
            }
    }
}

/** Bridges the view model to the Activity-only capabilities it needs. */
private class ShellHostImpl(private val activity: ShellActivity) : ShellViewModel.ShellHost {
    override fun openExternally(url: String) {
        activity.openExternally(url)
    }

    override fun createWebView(config: ShellConfig): WebView =
        ShellWebViewFactory.create(activity, config)

    override fun showFileChooser(
        callback: android.webkit.ValueCallback<Array<android.net.Uri>>,
        params: android.webkit.WebChromeClient.FileChooserParams,
    ): Boolean = activity.showFileChooser(callback, params)
}
