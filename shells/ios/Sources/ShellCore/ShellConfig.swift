import Foundation

/// The typed view of `appconfig.json`.
///
/// Mirrors `packages/config-schema/schema/appconfig.v1.json` and the Kotlin
/// `ShellConfig`. Only what the shell actually reads is modelled: the schema
/// carries fields that concern code generation or the studio, and modelling
/// those here would be a second place to keep in step for no benefit.
///
/// - Important: Every property is optional or defaulted. A shell built at
///   version N must never fail to decode a config written at version N+1 — an
///   app in a store cannot be patched as quickly as a config can be edited.
public struct ShellConfig: Codable, Sendable, Equatable {
    public var schemaVersion: Int
    public var app: AppIdentity
    public var branding: Branding
    public var navigation: Navigation
    public var linkRules: [LinkRule]
    public var webOverrides: WebOverrides
    public var offline: Offline
    public var permissions: Permissions
    public var build: BuildSettings

    public init(
        schemaVersion: Int = 1,
        app: AppIdentity,
        branding: Branding = Branding(),
        navigation: Navigation = Navigation(),
        linkRules: [LinkRule] = [],
        webOverrides: WebOverrides = WebOverrides(),
        offline: Offline = Offline(),
        permissions: Permissions = Permissions(),
        build: BuildSettings = BuildSettings()
    ) {
        self.schemaVersion = schemaVersion
        self.app = app
        self.branding = branding
        self.navigation = navigation
        self.linkRules = linkRules
        self.webOverrides = webOverrides
        self.offline = offline
        self.permissions = permissions
        self.build = build
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try container.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1
        app = try container.decode(AppIdentity.self, forKey: .app)
        branding = try container.decodeIfPresent(Branding.self, forKey: .branding) ?? Branding()
        navigation = try container.decodeIfPresent(Navigation.self, forKey: .navigation) ?? Navigation()
        linkRules = try container.decodeIfPresent([LinkRule].self, forKey: .linkRules) ?? []
        webOverrides = try container.decodeIfPresent(WebOverrides.self, forKey: .webOverrides) ?? WebOverrides()
        offline = try container.decodeIfPresent(Offline.self, forKey: .offline) ?? Offline()
        permissions = try container.decodeIfPresent(Permissions.self, forKey: .permissions) ?? Permissions()
        build = try container.decodeIfPresent(BuildSettings.self, forKey: .build) ?? BuildSettings()
    }
}

public struct AppIdentity: Codable, Sendable, Equatable {
    public var name: String
    public var bundleId: String
    public var versionName: String
    public var versionCode: Int
    public var initialUrl: String
    public var allowedOrigins: [String]

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        name = try c.decode(String.self, forKey: .name)
        bundleId = try c.decode(String.self, forKey: .bundleId)
        versionName = try c.decodeIfPresent(String.self, forKey: .versionName) ?? "1.0.0"
        versionCode = try c.decodeIfPresent(Int.self, forKey: .versionCode) ?? 1
        initialUrl = try c.decode(String.self, forKey: .initialUrl)
        allowedOrigins = try c.decodeIfPresent([String].self, forKey: .allowedOrigins) ?? []
    }

    public init(
        name: String,
        bundleId: String,
        versionName: String = "1.0.0",
        versionCode: Int = 1,
        initialUrl: String,
        allowedOrigins: [String] = []
    ) {
        self.name = name
        self.bundleId = bundleId
        self.versionName = versionName
        self.versionCode = versionCode
        self.initialUrl = initialUrl
        self.allowedOrigins = allowedOrigins
    }
}

public struct Branding: Codable, Sendable, Equatable {
    public var splash: Splash
    public var theme: Theme
    public var darkMode: String

    public init(splash: Splash = Splash(), theme: Theme = Theme(), darkMode: String = "system") {
        self.splash = splash
        self.theme = theme
        self.darkMode = darkMode
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        splash = try c.decodeIfPresent(Splash.self, forKey: .splash) ?? Splash()
        theme = try c.decodeIfPresent(Theme.self, forKey: .theme) ?? Theme()
        darkMode = try c.decodeIfPresent(String.self, forKey: .darkMode) ?? "system"
    }
}

public struct Splash: Codable, Sendable, Equatable {
    public var backgroundColor: String

    public init(backgroundColor: String = "#FFFFFF") {
        self.backgroundColor = backgroundColor
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        backgroundColor = try c.decodeIfPresent(String.self, forKey: .backgroundColor) ?? "#FFFFFF"
    }
}

public struct Theme: Codable, Sendable, Equatable {
    public var primary: String
    public var navBar: String
    public var tabBar: String
    public var statusBar: String

    public init(
        primary: String = "#2563EB",
        navBar: String = "#FFFFFF",
        tabBar: String = "#FFFFFF",
        statusBar: String = "dark-content"
    ) {
        self.primary = primary
        self.navBar = navBar
        self.tabBar = tabBar
        self.statusBar = statusBar
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        primary = try c.decodeIfPresent(String.self, forKey: .primary) ?? "#2563EB"
        navBar = try c.decodeIfPresent(String.self, forKey: .navBar) ?? "#FFFFFF"
        tabBar = try c.decodeIfPresent(String.self, forKey: .tabBar) ?? "#FFFFFF"
        statusBar = try c.decodeIfPresent(String.self, forKey: .statusBar) ?? "dark-content"
    }
}

public struct Navigation: Codable, Sendable, Equatable {
    public var topBar: TopBar
    public var tabBar: TabBar
    public var drawer: Drawer

    public init(topBar: TopBar = TopBar(), tabBar: TabBar = TabBar(), drawer: Drawer = Drawer()) {
        self.topBar = topBar
        self.tabBar = tabBar
        self.drawer = drawer
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        topBar = try c.decodeIfPresent(TopBar.self, forKey: .topBar) ?? TopBar()
        tabBar = try c.decodeIfPresent(TabBar.self, forKey: .tabBar) ?? TabBar()
        drawer = try c.decodeIfPresent(Drawer.self, forKey: .drawer) ?? Drawer()
    }
}

public struct TopBar: Codable, Sendable, Equatable {
    public var enabled: Bool
    public var titleSource: String
    public var staticTitle: String?
    public var actions: [TopBarAction]

    public init(
        enabled: Bool = true,
        titleSource: String = "documentTitle",
        staticTitle: String? = nil,
        actions: [TopBarAction] = []
    ) {
        self.enabled = enabled
        self.titleSource = titleSource
        self.staticTitle = staticTitle
        self.actions = actions
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
        titleSource = try c.decodeIfPresent(String.self, forKey: .titleSource) ?? "documentTitle"
        staticTitle = try c.decodeIfPresent(String.self, forKey: .staticTitle)
        actions = try c.decodeIfPresent([TopBarAction].self, forKey: .actions) ?? []
    }
}

public struct TopBarAction: Codable, Sendable, Equatable {
    public var id: String
    public var type: String
    public var label: LocalizedText?
}

public struct TabBar: Codable, Sendable, Equatable {
    public var enabled: Bool
    public var items: [TabItem]

    public init(enabled: Bool = false, items: [TabItem] = []) {
        self.enabled = enabled
        self.items = items
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? false
        items = try c.decodeIfPresent([TabItem].self, forKey: .items) ?? []
    }
}

public struct TabItem: Codable, Sendable, Equatable {
    public var id: String
    public var label: LocalizedText
    public var icon: String?
    public var url: String
    public var activePattern: String?
}

public struct Drawer: Codable, Sendable, Equatable {
    public var enabled: Bool
    public var items: [DrawerItem]

    public init(enabled: Bool = false, items: [DrawerItem] = []) {
        self.enabled = enabled
        self.items = items
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? false
        items = try c.decodeIfPresent([DrawerItem].self, forKey: .items) ?? []
    }
}

public struct DrawerItem: Codable, Sendable, Equatable {
    public var id: String
    public var label: LocalizedText
    public var icon: String?
    public var url: String?
    public var section: String?
}

public struct LinkRule: Codable, Sendable, Equatable {
    public var id: String
    public var pattern: String
    public var action: String

    public init(id: String, pattern: String, action: String) {
        self.id = id
        self.pattern = pattern
        self.action = action
    }
}

public struct WebOverrides: Codable, Sendable, Equatable {
    public var userAgentSuffix: String?
    public var headers: [String: String]
    public var persistCookies: Bool
    public var allowZoom: Bool
    public var pullToRefresh: Bool

    public init(
        userAgentSuffix: String? = nil,
        headers: [String: String] = [:],
        persistCookies: Bool = true,
        allowZoom: Bool = false,
        pullToRefresh: Bool = true
    ) {
        self.userAgentSuffix = userAgentSuffix
        self.headers = headers
        self.persistCookies = persistCookies
        self.allowZoom = allowZoom
        self.pullToRefresh = pullToRefresh
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        userAgentSuffix = try c.decodeIfPresent(String.self, forKey: .userAgentSuffix)
        headers = try c.decodeIfPresent([String: String].self, forKey: .headers) ?? [:]
        persistCookies = try c.decodeIfPresent(Bool.self, forKey: .persistCookies) ?? true
        allowZoom = try c.decodeIfPresent(Bool.self, forKey: .allowZoom) ?? false
        pullToRefresh = try c.decodeIfPresent(Bool.self, forKey: .pullToRefresh) ?? true
    }
}

public struct Offline: Codable, Sendable, Equatable {
    public var enabled: Bool
    public var fallbackBundle: String

    public init(enabled: Bool = true, fallbackBundle: String = "none") {
        self.enabled = enabled
        self.fallbackBundle = fallbackBundle
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        enabled = try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? true
        fallbackBundle = try c.decodeIfPresent(String.self, forKey: .fallbackBundle) ?? "none"
    }
}

public struct Permissions: Codable, Sendable, Equatable {
    public var camera: Bool
    public var microphone: Bool
    public var photoLibrary: Bool
    public var location: String
    public var notifications: Bool

    /// Whether the app may ask for location at all.
    public var wantsLocation: Bool { location != "none" }

    public init(
        camera: Bool = false,
        microphone: Bool = false,
        photoLibrary: Bool = false,
        location: String = "none",
        notifications: Bool = false
    ) {
        self.camera = camera
        self.microphone = microphone
        self.photoLibrary = photoLibrary
        self.location = location
        self.notifications = notifications
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        camera = try c.decodeIfPresent(Bool.self, forKey: .camera) ?? false
        microphone = try c.decodeIfPresent(Bool.self, forKey: .microphone) ?? false
        photoLibrary = try c.decodeIfPresent(Bool.self, forKey: .photoLibrary) ?? false
        location = try c.decodeIfPresent(String.self, forKey: .location) ?? "none"
        notifications = try c.decodeIfPresent(Bool.self, forKey: .notifications) ?? false
    }
}

public struct BuildSettings: Codable, Sendable, Equatable {
    public var ios: IosSettings
    public var orientation: String
    public var maximumWindows: Int

    public init(
        ios: IosSettings = IosSettings(),
        orientation: String = "any",
        maximumWindows: Int = 3
    ) {
        self.ios = ios
        self.orientation = orientation
        self.maximumWindows = maximumWindows
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        ios = try c.decodeIfPresent(IosSettings.self, forKey: .ios) ?? IosSettings()
        orientation = try c.decodeIfPresent(String.self, forKey: .orientation) ?? "any"
        maximumWindows = try c.decodeIfPresent(Int.self, forKey: .maximumWindows) ?? 3
    }
}

public struct IosSettings: Codable, Sendable, Equatable {
    public var minVersion: String

    public init(minVersion: String = "15.0") {
        self.minVersion = minVersion
    }

    public init(from decoder: any Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        minVersion = try c.decodeIfPresent(String.self, forKey: .minVersion) ?? "15.0"
    }
}

/// The decoder the shell reads its embedded config with.
public enum ShellJSON {
    /// A decoder that tolerates fields added by a newer schema.
    ///
    /// `JSONDecoder` ignores unknown keys by default, which is the behaviour
    /// wanted here and the Swift equivalent of Kotlin's `ignoreUnknownKeys`.
    public static func decoder() -> JSONDecoder {
        JSONDecoder()
    }
}
