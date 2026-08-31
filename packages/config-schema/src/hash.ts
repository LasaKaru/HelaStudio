/**
 * The three-way build cache key.
 *
 * A single hash over the whole config would mean every change forces a full
 * recompile. Splitting the key by what a change actually costs is the highest
 * leverage optimisation in the system (01_ENGINEERING_STANDARDS.md §2.1):
 * roughly 70-80% of user-triggered builds touch only assets or content, and
 * those take seconds rather than minutes.
 *
 *   codeKey    → full recompile      (~8 min iOS)
 *   assetKey   → repackage resources (~60 s)
 *   contentKey → patch config, re-sign (~40 s)
 */
import { blake3 } from '@noble/hashes/blake3';
import { canonicalBytes, type JsonObject, type JsonValue } from './canonical.js';

/** The three cache keys derived from a resolved configuration. */
export interface ConfigHashes {
  /** Changes here force a full native recompile. */
  readonly codeKey: string;
  /** Changes here need only a resource repackage. */
  readonly assetKey: string;
  /** Changes here need only a config patch and a re-sign. */
  readonly contentKey: string;
}

/** Inputs to hashing that come from outside the config document. */
export interface HashContext {
  /** Semver of the shell template the app is built from, for example `1.4.0`. */
  readonly shellVersion: string;
  /** Exact resolved plugin versions, as an id-to-version map. */
  readonly pluginLock?: Readonly<Record<string, string>>;
  /** Toolchain identity, for example `{ xcode: '26.1', agp: '8.9' }`. */
  readonly toolchain?: Readonly<Record<string, string>>;
}

const HASH_BYTES = 32;

/** Hashes canonical bytes with BLAKE3, returning lowercase hex. */
export function hashValue(value: JsonValue): string {
  const digest = blake3(canonicalBytes(value), { dkLen: HASH_BYTES });
  return Array.from(digest, (b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * Computes all three cache keys for a **resolved** configuration.
 *
 * The config must already have defaults resolved (see `resolveDefaults`), or an
 * omitted field and an explicitly-default field will hash differently and the
 * cache will miss on a no-op edit.
 */
export function computeHashes(resolved: JsonObject, context: HashContext): ConfigHashes {
  return {
    codeKey: hashValue(projectCode(resolved, context)),
    assetKey: hashValue(projectAsset(resolved)),
    contentKey: hashValue(projectContent(resolved)),
  };
}

/** Fields whose change requires recompiling native code. */
function projectCode(config: JsonObject, context: HashContext): JsonValue {
  const app = obj(config['app']);
  return {
    bundleId: app['bundleId'] ?? null,
    permissions: config['permissions'] ?? null,
    plugins: config['plugins'] ?? null,
    // Only the surface *types* affect generated code; their content is data.
    nativeSurfaceTypes: arr(config['nativeSurfaces']).map((s) => obj(s)['type'] ?? null),
    deepLinks: config['deepLinks'] ?? null,
    build: config['build'] ?? null,
    shellVersion: context.shellVersion,
    pluginLock: context.pluginLock ?? null,
    toolchain: context.toolchain ?? null,
  };
}

/** Fields whose change requires only repackaging resources. */
function projectAsset(config: JsonObject): JsonValue {
  const app = obj(config['app']);
  return {
    name: app['name'] ?? null,
    branding: config['branding'] ?? null,
    labels: collectLabels(config),
  };
}

/** Fields whose change requires only patching the embedded config and re-signing. */
function projectContent(config: JsonObject): JsonValue {
  const app = obj(config['app']);
  return {
    versionName: app['versionName'] ?? null,
    versionCode: app['versionCode'] ?? null,
    initialUrl: app['initialUrl'] ?? null,
    allowedOrigins: app['allowedOrigins'] ?? null,
    navigation: stripLabels(config['navigation'] ?? null),
    linkRules: config['linkRules'] ?? null,
    webOverrides: config['webOverrides'] ?? null,
    offline: config['offline'] ?? null,
    ota: config['ota'] ?? null,
    nativeSurfaceConfig: arr(config['nativeSurfaces']).map((s) => {
      const surface = obj(s);
      return { id: surface['id'] ?? null, config: surface['config'] ?? null };
    }),
  };
}

/** Keys that carry user-visible text or imagery, and so belong to the asset key. */
const LABEL_KEYS = new Set(['label', 'icon', 'staticTitle', 'section']);

/**
 * Collects every label and icon in navigation, in document order.
 *
 * Order matters: moving a tab is a resource change, and the key must reflect it.
 */
function collectLabels(config: JsonObject): JsonValue {
  const found: JsonValue[] = [];
  walk(config['navigation'] ?? null, (key, value) => {
    if (LABEL_KEYS.has(key)) found.push(value);
  });
  return found;
}

/** Returns navigation with label and icon fields removed, leaving only structure. */
function stripLabels(node: JsonValue): JsonValue {
  if (Array.isArray(node)) return node.map(stripLabels);
  if (typeof node !== 'object' || node === null) return node;
  const out: JsonObject = {};
  for (const [key, value] of Object.entries(node)) {
    if (!LABEL_KEYS.has(key)) out[key] = stripLabels(value);
  }
  return out;
}

function walk(node: JsonValue, visit: (key: string, value: JsonValue) => void): void {
  if (Array.isArray(node)) {
    for (const item of node) walk(item, visit);
    return;
  }
  if (typeof node !== 'object' || node === null) return;
  for (const [key, value] of Object.entries(node)) {
    visit(key, value);
    walk(value, visit);
  }
}

function obj(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}

function arr(value: JsonValue | undefined): JsonValue[] {
  return Array.isArray(value) ? value : [];
}
