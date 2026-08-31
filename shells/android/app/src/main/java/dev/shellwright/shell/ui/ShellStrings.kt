package dev.shellwright.shell.ui

import android.content.Context
import dev.shellwright.shell.R

/**
 * The user-facing strings the shell needs, resolved once.
 *
 * Exists so the view model holds text rather than a [Context]. Holding a
 * context in something that outlives a configuration change is the classic
 * Android leak, and it also makes the view model impossible to test on the JVM.
 * Resolving eagerly costs a handful of string lookups at startup.
 */
public data class ShellStrings(
    val actionShare: String,
    val actionRefresh: String,
    val actionSearch: String,
    val actionMenu: String,
) {
    /** The accessibility text a screen reader announces for an action. */
    public fun defaultActionLabel(type: String): String = when (type) {
        "share" -> actionShare
        "refresh" -> actionRefresh
        "search" -> actionSearch
        // Never the raw config `type`: a screen reader would announce
        // "custom", which tells the user nothing.
        else -> actionMenu
    }

    public companion object {
        /** Reads the strings for the device's current language. */
        public fun from(context: Context): ShellStrings = ShellStrings(
            actionShare = context.getString(R.string.action_share),
            actionRefresh = context.getString(R.string.action_refresh),
            actionSearch = context.getString(R.string.action_search),
            actionMenu = context.getString(R.string.action_menu),
        )
    }
}
