/**
 * Rules about uploaded assets.
 *
 * These need an asset store to consult, so they skip in the browser (where the
 * upload has not happened yet) and run server-side before a build. An icon with
 * an alpha channel is an automatic App Store rejection, so it is worth catching
 * before a macOS runner is ever woken up.
 */
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import type { RuleContext, ValidationRule } from './rule.js';

const MIN_ICON_SIZE = 1024;

/** Every asset reference must resolve to something that was actually uploaded. */
export const assetExistsRule: ValidationRule = {
  name: 'asset-exists',
  run(context: RuleContext): readonly Diagnostic[] {
    const assets = context.assets;
    if (assets === undefined) return [];

    return collectAssetRefs(context.config)
      .filter(({ ref }) => assets.lookup(ref) === undefined)
      .map(({ path }) =>
        diagnostic(
          DiagnosticCode.AssetMissing,
          'error',
          path,
          'This file is referenced but is not in your workspace. It may have been deleted, or the ' +
            'upload may not have finished. Upload it again.',
        ),
      );
  },
};

/** The source icon must be square and large enough for every store density. */
export const iconDimensionsRule: ValidationRule = {
  name: 'icon-dimensions',
  run(context: RuleContext): readonly Diagnostic[] {
    const assets = context.assets;
    if (assets === undefined) return [];

    const ref = asObject(context.config['branding'])['icon'];
    if (typeof ref !== 'string') return [];

    const metadata = assets.lookup(ref);
    if (metadata === undefined) return [];

    const path = pointer('branding', 'icon');
    if (metadata.width === metadata.height && metadata.width >= MIN_ICON_SIZE) return [];

    return [
      diagnostic(
        DiagnosticCode.IconDimensions,
        'error',
        path,
        `Your icon is ${String(metadata.width)} by ${String(metadata.height)} pixels. It must be square and ` +
          `at least ${String(MIN_ICON_SIZE)} by ${String(MIN_ICON_SIZE)}, because every smaller size is generated ` +
          'from it and the App Store requires that size for your listing.',
      ),
    ];
  },
};

/** iOS rejects an app icon that carries an alpha channel. */
export const iconAlphaRule: ValidationRule = {
  name: 'icon-alpha',
  run(context: RuleContext): readonly Diagnostic[] {
    const assets = context.assets;
    if (assets === undefined) return [];

    const ref = asObject(context.config['branding'])['icon'];
    if (typeof ref !== 'string') return [];

    if (assets.lookup(ref)?.hasAlpha !== true) return [];

    return [
      diagnostic(
        DiagnosticCode.IconAlpha,
        'error',
        pointer('branding', 'icon'),
        'Your icon has a transparent background. Apple rejects app icons with an alpha channel. ' +
          'Flatten it onto a solid background colour and upload it again.',
      ),
    ];
  },
};

const ASSET_PATTERN = /^asset:\/\/sha256-[0-9a-f]{64}$/;

/** Every asset reference in the document, with its JSON Pointer. */
function collectAssetRefs(
  config: JsonObject,
): readonly { readonly path: string; readonly ref: string }[] {
  const found: { path: string; ref: string }[] = [];
  walk(config, [], (path, value) => {
    if (ASSET_PATTERN.test(value)) found.push({ path, ref: value });
  });
  return found;
}

function walk(
  node: JsonValue,
  path: (string | number)[],
  visit: (path: string, value: string) => void,
): void {
  if (typeof node === 'string') {
    visit(pointer(...path), node);
    return;
  }
  if (Array.isArray(node)) {
    node.forEach((item, index) => {
      walk(item, [...path, index], visit);
    });
    return;
  }
  if (typeof node !== 'object' || node === null) return;
  for (const [key, value] of Object.entries(node)) {
    walk(value, [...path, key], visit);
  }
}

function asObject(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}
