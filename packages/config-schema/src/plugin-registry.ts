/**
 * The plugin registry a validator consults.
 *
 * The real registry is served by the control plane (S06) and populated from
 * plugin manifests (S10). This module defines the port plus a built-in snapshot
 * so that validation works offline, in the studio, and in tests.
 */

/** What a plugin declares about itself, as far as configuration validation cares. */
export interface PluginDescriptor {
  /** Stable plugin id, matching the manifest. */
  readonly id: string;
  /** Human-readable name, used in diagnostic messages. */
  readonly name: string;
  /** Minimum Android API level the plugin supports. */
  readonly minSdkAndroid: number;
  /** Minimum iOS version the plugin supports, for example `15.0`. */
  readonly minVersionIos: string;
  /** Device permissions the plugin cannot work without. */
  readonly requiredPermissions: readonly string[];
  /** Ids of plugins that cannot be enabled alongside this one. */
  readonly conflictsWith: readonly string[];
  /** Why those conflicts exist, keyed by the conflicting plugin id. */
  readonly conflictReasons: Readonly<Record<string, string>>;
  /** JSON Schema for this plugin's entry in `plugins`. */
  readonly configSchema: Readonly<Record<string, unknown>>;
}

/** A source of plugin descriptors. */
export interface PluginRegistry {
  /** Returns the descriptor for `id`, or undefined if no such plugin exists. */
  get(id: string): PluginDescriptor | undefined;
}

const objectSchema = (properties: Record<string, unknown>): Readonly<Record<string, unknown>> => ({
  type: 'object',
  properties,
  additionalProperties: false,
});

/**
 * Plugins known at Sprint 01.
 *
 * Deliberately small. Each entry is replaced by its real manifest in S10; the
 * shape here exists so the plugin rules have something to validate against.
 */
const BUILT_IN: readonly PluginDescriptor[] = [
  {
    id: 'haptics',
    name: 'Haptic Feedback',
    minSdkAndroid: 24,
    minVersionIos: '15.0',
    requiredPermissions: [],
    conflictsWith: [],
    conflictReasons: {},
    configSchema: objectSchema({}),
  },
  {
    id: 'biometric',
    name: 'Face ID and Fingerprint',
    minSdkAndroid: 24,
    minVersionIos: '15.0',
    requiredPermissions: ['biometric'],
    conflictsWith: [],
    conflictReasons: {},
    configSchema: objectSchema({
      promptReason: { type: 'string', minLength: 1, maxLength: 120 },
    }),
  },
  {
    id: 'qr-scanner',
    name: 'QR and Barcode Scanner',
    minSdkAndroid: 24,
    minVersionIos: '15.0',
    requiredPermissions: ['camera'],
    conflictsWith: ['scandit-scanner'],
    conflictReasons: { 'scandit-scanner': 'Both register a camera scanning surface.' },
    configSchema: objectSchema({
      formats: {
        type: 'array',
        items: { enum: ['qr', 'ean13', 'ean8', 'code128', 'code39', 'pdf417', 'dataMatrix'] },
      },
      beepOnScan: { type: 'boolean' },
      torchButton: { type: 'boolean' },
    }),
  },
  {
    id: 'scandit-scanner',
    name: 'Scandit Enterprise Scanning',
    minSdkAndroid: 26,
    minVersionIos: '16.0',
    requiredPermissions: ['camera'],
    conflictsWith: ['qr-scanner'],
    conflictReasons: { 'qr-scanner': 'Both register a camera scanning surface.' },
    configSchema: objectSchema({ licenceKeyRef: { type: 'string' } }),
  },
  {
    id: 'push',
    name: 'Push Notifications',
    minSdkAndroid: 24,
    minVersionIos: '15.0',
    requiredPermissions: ['notifications'],
    conflictsWith: [],
    conflictReasons: {},
    configSchema: objectSchema({
      provider: { enum: ['shellwright', 'onesignal', 'fcm'] },
      promptOnLaunch: { type: 'boolean' },
    }),
  },
  {
    id: 'iap',
    name: 'In-App Purchases',
    minSdkAndroid: 24,
    minVersionIos: '15.0',
    requiredPermissions: [],
    conflictsWith: [],
    conflictReasons: {},
    configSchema: objectSchema({
      productsUrl: { type: 'string', pattern: '^https://' },
    }),
  },
  {
    id: 'document-scanner',
    name: 'Document Scanner',
    minSdkAndroid: 26,
    minVersionIos: '16.0',
    requiredPermissions: ['camera'],
    conflictsWith: [],
    conflictReasons: {},
    configSchema: objectSchema({ outputFormat: { enum: ['pdf', 'jpeg'] } }),
  },
  {
    id: 'nfc',
    name: 'NFC Tag Scanner',
    minSdkAndroid: 26,
    minVersionIos: '15.0',
    requiredPermissions: [],
    conflictsWith: [],
    conflictReasons: {},
    configSchema: objectSchema({ readOnly: { type: 'boolean' } }),
  },
];

const byId = new Map(BUILT_IN.map((p) => [p.id, p]));

/** The registry of plugins compiled into this package. */
export const builtInPluginRegistry: PluginRegistry = {
  get: (id: string) => byId.get(id),
};

/** Every built-in plugin, for tests and for the `all-plugins` fixture. */
export const builtInPlugins: readonly PluginDescriptor[] = BUILT_IN;
