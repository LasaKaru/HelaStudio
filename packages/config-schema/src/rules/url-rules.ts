/**
 * Rules about origins and URLs.
 *
 * Getting these wrong produces the two worst first-run experiences: an app that
 * opens a blank screen, and an app whose every internal link bounces the user
 * out to their browser.
 */
import { DiagnosticCode, diagnostic, pointer, type Diagnostic } from '../diagnostics.js';
import type { JsonObject, JsonValue } from '../canonical.js';
import type { RuleContext, ValidationRule } from './rule.js';

/** Parses an origin string into a comparable form, or undefined if malformed. */
function originOf(url: string): string | undefined {
  try {
    return new URL(url).origin;
  } catch {
    return undefined;
  }
}

function allowedOrigins(config: JsonObject): Set<string> {
  const app = config['app'];
  const list =
    typeof app === 'object' && app !== null && !Array.isArray(app) ? app['allowedOrigins'] : null;
  const origins = new Set<string>();
  if (Array.isArray(list)) {
    for (const entry of list) {
      if (typeof entry === 'string') {
        const origin = originOf(entry);
        if (origin !== undefined) origins.add(origin);
      }
    }
  }
  return origins;
}

/** The start URL must be one of the origins the app treats as its own. */
export const initialUrlAllowedRule: ValidationRule = {
  name: 'initial-url-allowed',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const app = config['app'];
    if (typeof app !== 'object' || app === null || Array.isArray(app)) return [];
    const initialUrl = app['initialUrl'];
    if (typeof initialUrl !== 'string') return [];

    const origin = originOf(initialUrl);
    if (origin === undefined) return [];

    const origins = allowedOrigins(config);
    if (origins.size === 0 || origins.has(origin)) return [];

    return [
      diagnostic(
        DiagnosticCode.InitialUrlNotAllowed,
        'error',
        pointer('app', 'initialUrl'),
        `The start URL is on ${origin}, which is not in your allowed origins. ` +
          `Add "${origin}" to allowedOrigins, or point the start URL at an origin you have already listed.`,
      ),
    ];
  },
};

/** Every internal destination must fall under an allowed origin. */
export const originCoverageRule: ValidationRule = {
  name: 'origin-coverage',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const origins = allowedOrigins(config);
    if (origins.size === 0) return [];

    const found: Diagnostic[] = [];
    for (const { path, url } of internalDestinations(config)) {
      // A path is resolved against the start URL, so it is covered by definition.
      if (url.startsWith('/')) continue;
      const origin = originOf(url);
      if (origin === undefined || origins.has(origin)) continue;

      found.push(
        diagnostic(
          DiagnosticCode.OriginNotCovered,
          'error',
          path,
          `This destination is on ${origin}, which is not in your allowed origins, so it would open ` +
            `in the device browser instead of inside the app. Add "${origin}" to allowedOrigins.`,
        ),
      );
    }
    return found;
  },
};

/** Every destination that is expected to load inside the app. */
function* internalDestinations(
  config: JsonObject,
): Generator<{ readonly path: string; readonly url: string }> {
  const navigation = asObject(config['navigation']);

  const tabItems = asArray(asObject(navigation['tabBar'])['items']);
  for (const [index, item] of tabItems.entries()) {
    const url = asObject(item)['url'];
    if (typeof url === 'string') {
      yield { path: pointer('navigation', 'tabBar', 'items', index, 'url'), url };
    }
  }

  const drawerItems = asArray(asObject(navigation['drawer'])['items']);
  for (const [index, item] of drawerItems.entries()) {
    const url = asObject(item)['url'];
    if (typeof url === 'string') {
      yield { path: pointer('navigation', 'drawer', 'items', index, 'url'), url };
    }
  }
}

/** No plain-http URL anywhere: both platforms block cleartext by default. */
export const cleartextUrlRule: ValidationRule = {
  name: 'cleartext-url',
  run({ config }: RuleContext): readonly Diagnostic[] {
    const found: Diagnostic[] = [];
    walkStrings(config, [], (path, value) => {
      if (!value.toLowerCase().startsWith('http://')) return;
      found.push(
        diagnostic(
          DiagnosticCode.CleartextUrl,
          'error',
          path,
          'This is a plain http:// URL. iOS App Transport Security and Android cleartext policy both ' +
            'block it by default, so it would fail to load on a real device. Use https:// instead.',
        ),
      );
    });
    return found;
  },
};

function walkStrings(
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
      walkStrings(item, [...path, index], visit);
    });
    return;
  }
  if (typeof node !== 'object' || node === null) return;
  for (const [key, value] of Object.entries(node)) {
    // Injected CSS and JS are asset references, and `x-` blocks are opaque.
    if (key.startsWith('x-')) continue;
    walkStrings(value, [...path, key], visit);
  }
}

function asObject(value: JsonValue | undefined): JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value : {};
}

function asArray(value: JsonValue | undefined): JsonValue[] {
  return Array.isArray(value) ? value : [];
}
