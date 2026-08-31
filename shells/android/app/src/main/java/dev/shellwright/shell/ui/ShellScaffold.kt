package dev.shellwright.shell.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import dev.shellwright.shell.R
import androidx.compose.ui.viewinterop.AndroidView
import android.view.View
import androidx.compose.material3.ExperimentalMaterial3Api

/** One tab, already resolved for the current language. */
public data class TabUi(
    val id: String,
    val label: String,
    val icon: ShellIcon,
    val url: String,
)

/** One top bar action, already resolved. */
public data class ActionUi(
    val id: String,
    val type: String,
    val label: String,
)

/** Everything the chrome needs to draw itself. */
public data class ShellUiState(
    val title: String,
    val topBarEnabled: Boolean,
    val tabs: List<TabUi>,
    val selectedTabId: String?,
    val actions: List<ActionUi>,
    val progress: Int,
    val canGoBack: Boolean,
)

/**
 * The native chrome around the web view.
 *
 * ⚠️ This is drawn **before** the web view has content. Painting the bars from
 * the configured colours during the splash is what makes the app feel instant
 * rather than like a browser that took a moment to appear, and it is the
 * primary mitigation against an App Store guideline 4.2 rejection.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
public fun ShellScaffold(
    state: ShellUiState,
    colors: ShellColors,
    webView: View?,
    onTabSelected: (TabUi) -> Unit,
    onActionSelected: (ActionUi) -> Unit,
    onBack: () -> Unit,
) {
    Scaffold(
        topBar = {
            if (state.topBarEnabled) {
                Column {
                    TopAppBar(
                        title = {
                            Text(
                                text = state.title,
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        },
                        navigationIcon = {
                            if (state.canGoBack) {
                                IconButton(onClick = onBack) {
                                    Icon(
                                        imageVector = ShellIcon.Back.vector,
                                        contentDescription = stringResource(R.string.action_back),
                                    )
                                }
                            }
                        },
                        actions = {
                            state.actions.forEach { action ->
                                IconButton(onClick = { onActionSelected(action) }) {
                                    Icon(
                                        imageVector = ShellIcon.forAction(action.type).vector,
                                        contentDescription = action.label,
                                    )
                                }
                            }
                        },
                        colors = TopAppBarDefaults.topAppBarColors(
                            containerColor = colors.navBar,
                        ),
                    )

                    // Indeterminate-feeling but real progress: a page that is
                    // slow should look like it is working, not like it hung.
                    if (state.progress in 1..PROGRESS_HIDE_AT) {
                        LinearProgressIndicator(
                            progress = { state.progress / PROGRESS_MAX },
                            modifier = Modifier.fillMaxWidth(),
                            color = colors.primary,
                        )
                    }
                }
            }
        },
        bottomBar = {
            if (state.tabs.isNotEmpty()) {
                NavigationBar(containerColor = colors.tabBar) {
                    state.tabs.forEach { tab ->
                        NavigationBarItem(
                            selected = tab.id == state.selectedTabId,
                            onClick = { onTabSelected(tab) },
                            icon = {
                                Icon(
                                    imageVector = tab.icon.vector,
                                    contentDescription = null,
                                )
                            },
                            label = { Text(tab.label, maxLines = 1) },
                        )
                    }
                }
            }
        },
    ) { padding ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(MaterialTheme.colorScheme.background),
        ) {
            // Null until the web view is ready. The bars above are already
            // painted by then, which is the entire point.
            if (webView != null) {
                AndroidView(
                    factory = { webView },
                    modifier = Modifier.fillMaxSize(),
                )
            }
        }
    }
}

private const val PROGRESS_MAX = 100f
private const val PROGRESS_HIDE_AT = 99
