/* eslint-disable */
/**
 * GENERATED FILE — do not edit.
 * Source: schema/appconfig.v1.json
 * Regenerate with: pnpm --filter @shellwright/config-schema generate
 */
/**
 * Points editors at the schema so hand-written configs get autocomplete and inline help.
 */
export type SchemaURL = string;
/**
 * Which version of this schema the document was written against. Drives migration; never edit by hand.
 */
export type SchemaVersion = 1;
/**
 * The name shown under the icon and on your store listing. The App Store truncates beyond 30 characters.
 */
export type AppName = string;
/**
 * Reverse-DNS identifier, lowercase only. This is permanent — neither Apple nor Google lets you change it after the first release.
 */
export type BundleIdentifier = string;
/**
 * The version customers see, for example 1.4.0. Up to three dot-separated numbers.
 */
export type VersionName = string;
/**
 * The internal build number. Must increase with every store upload; Google Play rejects anything above 2100000000.
 */
export type VersionCode = number;
/**
 * The page the app opens on. Must be https — both platforms block plain http by default.
 */
export type StartURL = string;
/**
 * Origins treated as part of your app. The JavaScript bridge is injected only on these, and anything else is treated as an external link.
 *
 * @minItems 1
 * @maxItems 50
 */
export type AllowedOrigins = [SecureOrigin, ...SecureOrigin[]];
/**
 * Scheme, host, and optional port, with no path — for example https://app.acme.com.
 */
export type SecureOrigin = string;
/**
 * Source icon, at least 1024x1024, square, with no transparency. Every store density is generated from it.
 */
export type AppIcon = string;
/**
 * Fills the screen behind the logo.
 */
export type BackgroundColour = string;
/**
 * Centred on the splash background, inside the safe area.
 */
export type SplashLogo = string;
/**
 * Splash background used in dark mode.
 */
export type DarkBackgroundColour = string;
/**
 * Splash logo used in dark mode.
 */
export type DarkSplashLogo = string;
/**
 * Accent colour for controls, the refresh spinner, and selected tabs.
 */
export type PrimaryColour = string;
/**
 * Background of the native top bar.
 */
export type TopBarColour = string;
/**
 * Background of the native bottom tab bar.
 */
export type TabBarColour = string;
/**
 * Controls the colour of the clock and battery icons. Pick the one that contrasts with your top bar.
 */
export type StatusBarStyle = 'light-content' | 'dark-content' | 'hidden';
/**
 * Whether the app follows the device setting or pins itself to one appearance.
 */
export type DarkMode = 'system' | 'light' | 'dark';
/**
 * Turn off for a full-bleed layout where your website supplies its own header.
 */
export type ShowTheTopBar = boolean;
/**
 * Where the bar's title comes from: the page's title tag, a fixed string, or nothing.
 */
export type TitleSource = 'documentTitle' | 'static' | 'none';
/**
 * Used when the title source is set to a fixed string.
 */
export type FixedTitle = string;
/**
 * Buttons on the right of the top bar, shown in order.
 *
 * @maxItems 4
 */
export type ActionButtons =
  | []
  | [TopBarAction]
  | [TopBarAction, TopBarAction]
  | [TopBarAction, TopBarAction, TopBarAction]
  | [TopBarAction, TopBarAction, TopBarAction, TopBarAction];
/**
 * A stable id used to track this item across edits. Generated for you; changing it is treated as deleting and re-adding.
 */
export type ItemIdentifier = string;
/**
 * Built-in actions need no wiring. Choose 'custom' to receive a callback in your JavaScript instead.
 */
export type ActionType = 'share' | 'refresh' | 'search' | 'custom';
/**
 * Read aloud by screen readers. Required for custom actions.
 */
export type AccessibilityLabel = PlainText | Translations;
/**
 * Used for every language.
 */
export type PlainText = string;
/**
 * Icon shown in the bar.
 */
export type Icon = BuiltInIcon | AssetReference;
/**
 * Name from the built-in icon set.
 */
export type BuiltInIcon = string;
/**
 * A content-addressed pointer to an uploaded file. The studio fills this in when you upload; identical files are stored once.
 */
export type AssetReference = string;
/**
 * Turn on to show a native bottom tab bar.
 */
export type ShowTheTabBar = boolean;
/**
 * Up to five tabs. iOS collapses anything beyond five into a 'More' tab, which reads poorly.
 *
 * @maxItems 8
 */
export type Tabs =
  | []
  | [Tab]
  | [Tab, Tab]
  | [Tab, Tab, Tab]
  | [Tab, Tab, Tab, Tab]
  | [Tab, Tab, Tab, Tab, Tab]
  | [Tab, Tab, Tab, Tab, Tab, Tab]
  | [Tab, Tab, Tab, Tab, Tab, Tab, Tab]
  | [Tab, Tab, Tab, Tab, Tab, Tab, Tab, Tab];
/**
 * Keep it to one short word; the tab bar is narrow.
 */
export type TabLabel = PlainText | Translations;
/**
 * A built-in icon name or an uploaded image.
 */
export type TabIcon = BuiltInIcon | AssetReference;
/**
 * Where the tab navigates. A path is resolved against your start URL.
 */
export type Destination = string;
/**
 * Regular expression deciding when this tab shows as selected.
 */
export type ActiveWhile = string;
/**
 * Turn on to add a slide-out navigation menu.
 */
export type ShowTheDrawer = boolean;
/**
 * Text shown in the menu.
 */
export type ItemLabel = PlainText | Translations;
/**
 * Icon shown beside the label.
 */
export type ItemIcon = BuiltInIcon | AssetReference;
/**
 * Where the item navigates. Omit for a section heading.
 */
export type Destination1 = string;
/**
 * Groups items under a shared heading in the menu.
 */
export type Section = string;
/**
 * @maxItems 50
 */
export type MenuItems = DrawerItem[];
/**
 * A regular expression matched against the full URL. Use .* to match everything.
 */
export type URLPattern = string;
/**
 * Where a matching link opens: inside the app, in a modal, in a reader view, in the device browser, or blocked entirely.
 */
export type OpenIn = 'internal' | 'modal' | 'readerModal' | 'externalBrowser' | 'block';
/**
 * Ordered rules deciding where each link opens. The first matching rule wins, so put specific patterns above general ones and end with a catch-all.
 *
 * @maxItems 500
 */
export type LinkRules = LinkRule[];
/**
 * Appended to the browser user agent so your server can detect the app.
 */
export type UserAgentSuffix = string;
/**
 * A content-addressed pointer to an uploaded file. The studio fills this in when you upload; identical files are stored once.
 */
export type AssetReference1 = string;
/**
 * A content-addressed pointer to an uploaded file. The studio fills this in when you upload; identical files are stored once.
 */
export type AssetReference2 = string;
/**
 * Preserves cookies between launches so users are not signed out every time.
 */
export type KeepUsersSignedIn = boolean;
/**
 * Lets users pinch to zoom. Leave off for app-like behaviour, on for accessibility.
 */
export type AllowPinchZoom = boolean;
/**
 * Adds the native pull-down gesture to reload the page.
 */
export type PullToRefresh = boolean;
/**
 * Show a branded offline screen instead of a browser error.
 */
export type HandleOffline = boolean;
/**
 * A content-addressed pointer to an uploaded file. The studio fills this in when you upload; identical files are stored once.
 */
export type AssetReference3 = string;
/**
 * Which pages to cache for offline use: none, chosen automatically, or a bundle you supply.
 */
export type OfflineBundle = 'none' | 'auto' | 'custom';
/**
 * Fully native screens rendered outside the web view, such as onboarding or a settings page. These are what make the app more than a repackaged website.
 *
 * @maxItems 20
 */
export type NativeSurfaces =
  | []
  | [NativeSurface]
  | [NativeSurface, NativeSurface]
  | [NativeSurface, NativeSurface, NativeSurface]
  | [NativeSurface, NativeSurface, NativeSurface, NativeSurface]
  | [NativeSurface, NativeSurface, NativeSurface, NativeSurface, NativeSurface]
  | [NativeSurface, NativeSurface, NativeSurface, NativeSurface, NativeSurface, NativeSurface]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ]
  | [
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface,
      NativeSurface
    ];
/**
 * Which native screen to render.
 */
export type SurfaceType = 'onboarding' | 'settings' | 'about' | 'paywall';
/**
 * Displays on first launch only, then never again.
 */
export type ShowOnlyOnce = boolean;
/**
 * Lets the app fetch new web bundles on launch.
 */
export type EnableOTAUpdates = boolean;
/**
 * Which stream of updates this build follows.
 */
export type ReleaseChannel = 'production' | 'beta' | 'development';
/**
 * Share of users who receive the update. Start small and raise it once you see clean crash numbers.
 */
export type RolloutPercentage = number;
/**
 * Domains whose links open in the app. Each needs a verification file hosted at its root.
 *
 * @maxItems 20
 */
export type LinkedDomains =
  | []
  | [DomainName]
  | [DomainName, DomainName]
  | [DomainName, DomainName, DomainName]
  | [DomainName, DomainName, DomainName, DomainName]
  | [DomainName, DomainName, DomainName, DomainName, DomainName]
  | [DomainName, DomainName, DomainName, DomainName, DomainName, DomainName]
  | [DomainName, DomainName, DomainName, DomainName, DomainName, DomainName, DomainName]
  | [DomainName, DomainName, DomainName, DomainName, DomainName, DomainName, DomainName, DomainName]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ]
  | [
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName,
      DomainName
    ];
export type DomainName = string;
/**
 * A private scheme such as 'acme', giving you acme:// links. Lowercase letters and digits only.
 */
export type CustomURLScheme = string;
/**
 * Needed for photo capture and barcode scanning.
 */
export type Camera = boolean;
/**
 * Needed for audio recording and video calls.
 */
export type Microphone = boolean;
/**
 * Needed to attach existing photos to web forms.
 */
export type PhotoLibrary = boolean;
/**
 * Choose 'whenInUse' unless you genuinely track location in the background — 'always' invites extra store scrutiny.
 */
export type Location = 'none' | 'whenInUse' | 'always';
/**
 * Needed to send push notifications.
 */
export type Notifications = boolean;
/**
 * Needed to read the device address book.
 */
export type Contacts = boolean;
/**
 * Needed to read or add calendar events.
 */
export type Calendar = boolean;
/**
 * Needed to unlock the app with biometrics.
 */
export type FaceIDAndFingerprint = boolean;
/**
 * API level 24 is Android 7.0 and covers almost every active device.
 */
export type MinimumAndroidVersion = number;
/**
 * Google requires this to stay near the latest release.
 */
export type TargetAndroidVersion = number;
/**
 * The oldest iOS release the app installs on.
 */
export type MinimumIOSVersion = string;
/**
 * Which way the app may rotate.
 */
export type ScreenOrientation = 'any' | 'portrait' | 'landscape';
/**
 * Caps how many modal web views can stack. Each one holds memory, so keep it low.
 */
export type MaximumOpenWindows = number;

/**
 * The single source of truth for a generated app. Every native project, cache key, and studio form is derived from this document.
 */
export interface ShellwrightAppConfiguration {
  $schema?: SchemaURL;
  schemaVersion: SchemaVersion;
  app: AppIdentity;
  branding?: Branding;
  navigation?: Navigation;
  linkRules?: LinkRules;
  webOverrides?: WebOverrides;
  offline?: OfflineBehaviour;
  nativeSurfaces?: NativeSurfaces;
  plugins?: Plugins;
  ota?: OverTheAirUpdates;
  deepLinks?: DeepLinks;
  permissions?: Permissions;
  build?: BuildSettings;
  [k: string]: ExtensionObject;
}
/**
 * Who the app is and where it starts. These values appear in the stores and cannot be changed casually after publication.
 */
export interface AppIdentity {
  name: AppName;
  bundleId: BundleIdentifier;
  versionName?: VersionName;
  versionCode?: VersionCode;
  initialUrl: StartURL;
  allowedOrigins?: AllowedOrigins;
}
/**
 * Icon, splash screen, and colours. Changing anything here rebuilds resources only, which is far faster than a full recompile.
 */
export interface Branding {
  icon?: AppIcon;
  splash?: SplashScreen;
  theme?: ThemeColours;
  darkMode?: DarkMode;
}
/**
 * What shows while the app starts. Keep it simple — it is on screen for under a second.
 */
export interface SplashScreen {
  backgroundColor?: BackgroundColour;
  logo?: SplashLogo;
  dark?: DarkAppearanceOverrides;
}
/**
 * Replaces the splash colours when the device is in dark mode.
 */
export interface DarkAppearanceOverrides {
  backgroundColor?: DarkBackgroundColour;
  logo?: DarkSplashLogo;
}
/**
 * The native chrome around your website. Match these to your site so the seam is invisible.
 */
export interface ThemeColours {
  primary?: PrimaryColour;
  navBar?: TopBarColour;
  tabBar?: TabBarColour;
  statusBar?: StatusBarStyle;
}
/**
 * The native chrome. At least one of these should be enabled — an app with no native navigation is very likely to be rejected under App Store guideline 4.2.
 */
export interface Navigation {
  topBar?: TopBar;
  tabBar?: BottomTabBar;
  drawer?: SideDrawer;
}
/**
 * The native bar across the top of the screen.
 */
export interface TopBar {
  enabled?: ShowTheTopBar;
  titleSource?: TitleSource;
  staticTitle?: FixedTitle;
  actions?: ActionButtons;
}
export interface TopBarAction {
  id: ItemIdentifier;
  type: ActionType;
  label?: AccessibilityLabel;
  icon?: Icon;
}
/**
 * Per-language text. 'default' is used for any language not listed.
 */
export interface Translations {
  [k: string]: string;
}
/**
 * The strongest signal to a store reviewer that this is an app rather than a website.
 */
export interface BottomTabBar {
  enabled?: ShowTheTabBar;
  items?: Tabs;
}
export interface Tab {
  id: ItemIdentifier;
  label: TabLabel;
  icon?: TabIcon;
  url: Destination;
  activePattern?: ActiveWhile;
}
/**
 * A slide-out menu, useful when you have more destinations than fit in a tab bar.
 */
export interface SideDrawer {
  enabled?: ShowTheDrawer;
  items?: MenuItems;
}
export interface DrawerItem {
  id: ItemIdentifier;
  label: ItemLabel;
  icon?: ItemIcon;
  url?: Destination1;
  section?: Section;
}
/**
 * Matches a URL and decides where it opens.
 */
export interface LinkRule {
  id: ItemIdentifier;
  pattern: URLPattern;
  action: OpenIn;
}
/**
 * Adjustments applied to your website when it runs inside the app.
 */
export interface WebOverrides {
  userAgentSuffix?: UserAgentSuffix;
  headers?: ExtraRequestHeaders;
  injectCss?: AssetReference1;
  injectJs?: AssetReference2;
  persistCookies?: KeepUsersSignedIn;
  allowZoom?: AllowPinchZoom;
  pullToRefresh?: PullToRefresh;
}
/**
 * Sent with every page request the app makes. Never put a secret here — the config is stored, hashed, and exportable.
 */
export interface ExtraRequestHeaders {
  [k: string]: string;
}
/**
 * What the app does with no connection. A blank screen is a common reason for store rejection.
 */
export interface OfflineBehaviour {
  enabled?: HandleOffline;
  offlinePage?: AssetReference3;
  fallbackBundle?: OfflineBundle;
}
export interface NativeSurface {
  id: ItemIdentifier;
  type: SurfaceType;
  showOnce?: ShowOnlyOnce;
  config?: SurfaceConfiguration;
}
/**
 * Content for this surface, validated against the surface type's own schema.
 */
export interface SurfaceConfiguration {}
/**
 * Native capabilities to include, keyed by plugin id. Each value is validated against that plugin's own configuration schema.
 */
export interface Plugins {
  [k: string]: {};
}
/**
 * Ship web bundle changes without waiting for store review.
 */
export interface OverTheAirUpdates {
  enabled?: EnableOTAUpdates;
  channel?: ReleaseChannel;
  rolloutPercent?: RolloutPercentage;
}
/**
 * Lets links to your website open directly in the app.
 */
export interface DeepLinks {
  universalLinks?: LinkedDomains;
  customScheme?: CustomURLScheme;
}
/**
 * Device capabilities the app may ask for. Request only what you use — an unjustified permission is a common rejection cause.
 */
export interface Permissions {
  camera?: Camera;
  microphone?: Microphone;
  photoLibrary?: PhotoLibrary;
  location?: Location;
  notifications?: Notifications;
  contacts?: Contacts;
  calendar?: Calendar;
  biometric?: FaceIDAndFingerprint;
}
/**
 * Toolchain and platform floors. Leave these alone unless a plugin requires a change.
 */
export interface BuildSettings {
  android?: AndroidBuildSettings;
  ios?: IOSBuildSettings;
  orientation?: ScreenOrientation;
  maximumWindows?: MaximumOpenWindows;
}
export interface AndroidBuildSettings {
  minSdk?: MinimumAndroidVersion;
  targetSdk?: TargetAndroidVersion;
}
export interface IOSBuildSettings {
  minVersion?: MinimumIOSVersion;
}
/**
 * An escape hatch for data this schema does not model. Ignored by code generation and excluded from every cache key.
 *
 * This interface was referenced by `ShellwrightAppConfiguration`'s JSON-Schema definition
 * via the `patternProperty` "^x-".
 */
export interface ExtensionObject {}
