/**
 * Rules that predict a store rejection.
 *
 * Apple's guideline 4.2 rejects "repackaged websites", and an unjustified
 * permission is one of the most common rejection reasons on both stores. These
 * warnings become the Store Readiness Score in S16 — catching a rejection at
 * config-save time costs nothing, catching it after submission costs a week.
 */
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import type { RuleContext, ValidationRule } from './rule.js';

/** iOS collapses tabs beyond the fifth into a "More" tab, which reads badly. */
export const tabCountRule: ValidationRule = {
  name: 'tab-count',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const items = asArray(asObject(asObject(config['navigation'])['tabBar'])['items']);
    if (items.length <= 5) return [];

    return [
      diagnostic(
        DiagnosticCode.TabCountHigh,
        'warning',
        pointer('navigation', 'tabBar', 'items'),
        `You have ${String(items.length)} tabs. iOS shows only the first four and hides the rest behind a ` +
          '"More" tab, which most users never open. Keep five or fewer, and move the rest into a drawer.',
      ),
    ];
  },
};

/** An app with no native surface at all is very likely to be rejected. */
export const nativeFeaturesRule: ValidationRule = {
  name: 'native-features',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const navigation = asObject(config['navigation']);
    const hasTabs = isEnabledWithItems(navigation['tabBar']);
    const hasDrawer = isEnabledWithItems(navigation['drawer']);
    const hasPlugins = Object.keys(asObject(config['plugins'])).length > 0;
    const hasSurfaces = asArray(config['nativeSurfaces']).length > 0;
    const hasDeepLinks = asArray(asObject(config['deepLinks'])['universalLinks']).length > 0;

    if (hasTabs || hasDrawer || hasPlugins || hasSurfaces || hasDeepLinks) return [];

    return [
      diagnostic(
        DiagnosticCode.NoNativeFeatures,
        'warning',
        '',
        'This app has no native navigation, no plugins, and no native screens, so it is a web page in a ' +
          'frame. Apple rejects these under App Store guideline 4.2. Add a tab bar or drawer, enable a ' +
          'capability such as push notifications or biometric unlock, or add an onboarding screen.',
      ),
    ];
  },
};

function isEnabledWithItems(node: JsonValue | undefined): boolean {
  const value = asObject(node);
  return value['enabled'] === true && asArray(value['items']).length > 0;
}

/** Which plugin capability, if any, justifies each permission. */
const PERMISSION_JUSTIFICATIONS: Readonly<Record<string, readonly string[]>> = {
  camera: ['qr-scanner', 'scandit-scanner', 'document-scanner'],
  microphone: [],
  photoLibrary: [],
  notifications: ['push'],
  contacts: [],
  calendar: [],
  biometric: ['biometric'],
};

/**
 * Permissions with no plugin behind them.
 *
 * Camera, microphone, and photo library are also reachable straight from a web
 * form, so those stay a warning rather than an error — but an unexplained
 * permission prompt is still one of the most common rejection reasons.
 */
export const permissionJustifiedRule: ValidationRule = {
  name: 'permission-justified',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const permissions = asObject(config['permissions']);
    const enabledPlugins = new Set(Object.keys(asObject(config['plugins'])));
    const found: Diagnostic[] = [];

    for (const [name, value] of Object.entries(permissions)) {
      if (!isRequested(value)) continue;
      const justifiers = PERMISSION_JUSTIFICATIONS[name];
      if (justifiers === undefined) continue;
      if (justifiers.some((id) => enabledPlugins.has(id))) continue;

      found.push(
        diagnostic(
          DiagnosticCode.PermissionUnjustified,
          'warning',
          pointer('permissions', name),
          `Nothing in this configuration uses the ${name} permission. Both stores ask why a permission is ` +
            'requested, and an unexplained prompt is a common rejection reason. ' +
            (justifiers.length > 0
              ? `Enable the ${justifiers.join(' or ')} plugin, or turn this permission off.`
              : 'Turn it off unless your website genuinely asks for it.'),
        ),
      );
    }
    return found;
  },
};

function isRequested(value: JsonValue): boolean {
  if (typeof value === 'boolean') return value;
  return typeof value === 'string' && value !== 'none';
}

/** Ids in a list must be unique, or the studio cannot track items across edits. */
export const duplicateIdRule: ValidationRule = {
  name: 'duplicate-id',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const navigation = asObject(config['navigation']);
    const found: Diagnostic[] = [];

    const lists: readonly (readonly [readonly (string | number)[], JsonValue | undefined])[] = [
      [['navigation', 'tabBar', 'items'], asObject(navigation['tabBar'])['items']],
      [['navigation', 'drawer', 'items'], asObject(navigation['drawer'])['items']],
      [['navigation', 'topBar', 'actions'], asObject(navigation['topBar'])['actions']],
      [['linkRules'], config['linkRules']],
      [['nativeSurfaces'], config['nativeSurfaces']],
    ];

    for (const [path, list] of lists) {
      const seen = new Map<string, number>();
      for (const [index, item] of asArray(list).entries()) {
        const id = asObject(item)['id'];
        if (typeof id !== 'string') continue;
        const first = seen.get(id);
        if (first === undefined) {
          seen.set(id, index);
          continue;
        }
        found.push(
          diagnostic(
            DiagnosticCode.DuplicateItemId,
            'error',
            pointer(...path, index, 'id'),
            `The id "${id}" is already used by item ${String(first + 1)} in this list. ` +
              'Every item needs its own id so edits and reordering are tracked correctly.',
          ),
        );
      }
    }
    return found;
  },
};

function asObject(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}

function asArray(value: JsonValue | undefined): JsonValue[] {
  return Array.isArray(value) ? value : [];
}
