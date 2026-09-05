package dev.shellwright.shell.ui

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.List
import androidx.compose.material.icons.filled.AccountCircle
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Share
import androidx.compose.material.icons.filled.ShoppingCart
import androidx.compose.material.icons.filled.Star
import androidx.compose.ui.graphics.vector.ImageVector

/**
 * The built-in icon set a config may name.
 *
 * Deliberately small. A customer naming an icon that does not exist gets
 * [Star] rather than a crash or an empty gap, because an app that fails to draw
 * a tab is worse than one that draws a slightly wrong tab. Custom uploaded
 * icons arrive with asset handling in Sprint 04.
 */
public enum class ShellIcon(public val vector: ImageVector) {
    Home(Icons.Filled.Home),
    Package(Icons.Filled.ShoppingCart),
    User(Icons.Filled.AccountCircle),
    Settings(Icons.Filled.Settings),
    Search(Icons.Filled.Search),
    Share(Icons.Filled.Share),
    Refresh(Icons.Filled.Refresh),
    Menu(Icons.AutoMirrored.Filled.List),
    Back(Icons.AutoMirrored.Filled.ArrowBack),
    Fallback(Icons.Filled.Star),
    ;

    public companion object {
        private val byName = entries.associateBy { it.name.lowercase() }

        /** Resolves a config icon name, falling back rather than failing. */
        public fun forName(name: String?): ShellIcon =
            byName[name?.lowercase()] ?: Fallback

        /** The icon for a built-in top bar action type. */
        public fun forAction(type: String): ShellIcon = when (type) {
            "share" -> Share
            "refresh" -> Refresh
            "search" -> Search
            else -> Menu
        }
    }
}
