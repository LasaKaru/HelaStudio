package dev.shellwright.shell.config

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

/**
 * The typed view of `appconfig.json`.
 *
 * Mirrors `packages/config-schema/schema/appconfig.v1.json`. Only what the shell
 * actually reads is modelled: the schema carries fields that concern code
 * generation or the studio, and modelling those here would be a second place to
 * keep in step for no benefit.
 *
 * ⚠️ [ShellJson] is configured with `ignoreUnknownKeys`. A shell built at
 * version N must never crash on a config written at version N+1 — an app in a
 * store cannot be patched as quickly as a config can be edited.
 */
@Serializable
public data class ShellConfig(
    val schemaVersion: Int = 1,
    val app: AppIdentity,
    val branding: Branding = Branding(),
    val navigation: Navigation = Navigation(),
    val linkRules: List<LinkRule> = emptyList(),
    val webOverrides: WebOverrides = WebOverrides(),
    val offline: Offline = Offline(),
    val permissions: Permissions = Permissions(),
    val build: BuildSettings = BuildSettings(),
)

@Serializable
public data class AppIdentity(
    val name: String,
    val bundleId: String,
    val versionName: String = "1.0.0",
    val versionCode: Int = 1,
    val initialUrl: String,
    val allowedOrigins: List<String> = emptyList(),
)

@Serializable
public data class Branding(
    val splash: Splash = Splash(),
    val theme: Theme = Theme(),
    val darkMode: String = "system",
)

@Serializable
public data class Splash(
    val backgroundColor: String = "#FFFFFF",
)

@Serializable
public data class Theme(
    val primary: String = "#2563EB",
    val navBar: String = "#FFFFFF",
    val tabBar: String = "#FFFFFF",
    val statusBar: String = "dark-content",
)

@Serializable
public data class Navigation(
    val topBar: TopBar = TopBar(),
    val tabBar: TabBar = TabBar(),
    val drawer: Drawer = Drawer(),
)

@Serializable
public data class TopBar(
    val enabled: Boolean = true,
    val titleSource: String = "documentTitle",
    val staticTitle: String? = null,
    val actions: List<TopBarAction> = emptyList(),
)

@Serializable
public data class TopBarAction(
    val id: String,
    val type: String,
    val label: LocalizedText? = null,
)

@Serializable
public data class TabBar(
    val enabled: Boolean = false,
    val items: List<TabItem> = emptyList(),
)

@Serializable
public data class TabItem(
    val id: String,
    val label: LocalizedText,
    val icon: String? = null,
    val url: String,
    val activePattern: String? = null,
)

@Serializable
public data class Drawer(
    val enabled: Boolean = false,
    val items: List<DrawerItem> = emptyList(),
)

@Serializable
public data class DrawerItem(
    val id: String,
    val label: LocalizedText,
    val icon: String? = null,
    val url: String? = null,
    val section: String? = null,
)

@Serializable
public data class LinkRule(
    val id: String,
    val pattern: String,
    val action: String,
)

@Serializable
public data class WebOverrides(
    val userAgentSuffix: String? = null,
    val headers: Map<String, String> = emptyMap(),
    val persistCookies: Boolean = true,
    val allowZoom: Boolean = false,
    val pullToRefresh: Boolean = true,
)

@Serializable
public data class Offline(
    val enabled: Boolean = true,
    val fallbackBundle: String = "none",
)

@Serializable
public data class Permissions(
    val camera: Boolean = false,
    val microphone: Boolean = false,
    val photoLibrary: Boolean = false,
    val location: String = "none",
    val notifications: Boolean = false,
) {
    /** True when the app may ask for location at all. */
    public val wantsLocation: Boolean get() = location != "none"
}

@Serializable
public data class BuildSettings(
    @SerialName("android") val androidSettings: AndroidSettings = AndroidSettings(),
    val orientation: String = "any",
    val maximumWindows: Int = 3,
)

@Serializable
public data class AndroidSettings(
    val minSdk: Int = 24,
    val targetSdk: Int = 36,
)
