/**
 * The defensive branches.
 *
 * The studio validates on every keystroke, so it routinely hands this package a
 * half-typed document: a link rule with no pattern yet, a plugin whose settings
 * are still being filled in, a URL that is not yet a URL. None of that may
 * throw, and none of it may produce a misleading diagnostic.
 */
import { describe, expect, it } from 'vitest';
import { DiagnosticCode } from '../src/diagnostics.js';
import { builtInPluginRegistry } from '../src/plugin-registry.js';
import { assetExistsRule } from '../src/rules/asset-rules.js';
import { catchAllRule, regexSafetyRule, unreachableRuleRule } from '../src/rules/link-rules.js';
import {
  pluginConflictRule,
  pluginKnownRule,
  pluginPlatformFloorRule,
} from '../src/rules/plugin-rules.js';
import { originCoverageRule } from '../src/rules/url-rules.js';
import { constraint, typeName, validate } from '../src/validate.js';
import type { JsonObject } from '../src/canonical.js';
import type { RuleContext } from '../src/rules/rule.js';

function ctx(config: JsonObject): RuleContext {
  return { config, plugins: builtInPluginRegistry };
}

describe('half-typed link rules', () => {
  it('ignores a rule that has no pattern yet', () => {
    const config: JsonObject = {
      linkRules: [{ id: 'a' }, { id: 'b', pattern: '.*', action: 'internal' }],
    };
    expect(regexSafetyRule.run(ctx(config))).toEqual([]);
    expect(unreachableRuleRule.run(ctx(config))).toEqual([]);
    expect(catchAllRule.run(ctx(config))).toEqual([]);
  });

  it('ignores a rule that is not an object at all', () => {
    const config: JsonObject = { linkRules: ['nonsense', 42, null] };
    expect(() => regexSafetyRule.run(ctx(config))).not.toThrow();
    expect(unreachableRuleRule.run(ctx(config))).toEqual([]);
  });

  it('treats linkRules of the wrong type as absent', () => {
    expect(catchAllRule.run(ctx({ linkRules: 'not an array' }))).toEqual([]);
  });

  it('checks a tab activePattern as well as a link rule pattern', () => {
    const config: JsonObject = {
      navigation: {
        tabBar: { enabled: true, items: [{ id: 'a', label: 'A', activePattern: '^(a+)+$' }] },
      },
    };
    const found = regexSafetyRule.run(ctx(config));
    expect(found[0]?.code).toBe(DiagnosticCode.RegexCatastrophic);
    expect(found[0]?.path).toBe('/navigation/tabBar/items/0/activePattern');
  });
});

describe('half-typed plugins', () => {
  it('reports an unknown plugin without also guessing at its settings', () => {
    const config: JsonObject = { plugins: { mystery: { anything: true } } };
    expect(pluginKnownRule.run(ctx(config))[0]?.code).toBe(DiagnosticCode.PluginUnknown);
    expect(pluginConflictRule.run(ctx(config))).toEqual([]);
    expect(pluginPlatformFloorRule.run(ctx(config))).toEqual([]);
  });

  it('falls back to the platform defaults when build settings are missing', () => {
    // No `build` block at all: minSdk 24 and iOS 15.0 are assumed, so a plugin
    // needing API 26 is still caught.
    const found = pluginPlatformFloorRule.run(ctx({ plugins: { 'document-scanner': {} } }));
    expect(found.map((d) => d.code)).toContain(DiagnosticCode.PluginMinSdk);
  });

  it('ignores build settings of the wrong type', () => {
    const config: JsonObject = {
      plugins: { haptics: {} },
      build: { android: { minSdk: 'twenty-four' }, ios: { minVersion: 15 } },
    };
    expect(pluginPlatformFloorRule.run(ctx(config))).toEqual([]);
  });

  it('reports a conflicting pair once, whichever order it is declared in', () => {
    const forward = pluginConflictRule.run(
      ctx({ plugins: { 'qr-scanner': {}, 'scandit-scanner': {} } }),
    );
    const reverse = pluginConflictRule.run(
      ctx({ plugins: { 'scandit-scanner': {}, 'qr-scanner': {} } }),
    );
    expect(forward).toHaveLength(1);
    expect(reverse).toHaveLength(1);
    expect(forward[0]?.path).toBe(reverse[0]?.path);
  });

  it('does not report a conflict against a plugin that is not enabled', () => {
    expect(pluginConflictRule.run(ctx({ plugins: { 'qr-scanner': {} } }))).toEqual([]);
  });
});

describe('malformed URLs', () => {
  it('does not report a destination that is not parseable as a URL', () => {
    const config: JsonObject = {
      app: { allowedOrigins: ['https://app.acme.com'] },
      navigation: { drawer: { enabled: true, items: [{ id: 'a', label: 'A', url: 'https://' }] } },
    };
    expect(originCoverageRule.run(ctx(config))).toEqual([]);
  });

  it('skips origin coverage entirely when no origins are declared yet', () => {
    const config: JsonObject = {
      navigation: {
        drawer: { enabled: true, items: [{ id: 'a', label: 'A', url: 'https://x.example.com' }] },
      },
    };
    expect(originCoverageRule.run(ctx(config))).toEqual([]);
  });

  it('ignores a malformed entry inside allowedOrigins', () => {
    const config: JsonObject = {
      app: { allowedOrigins: ['not a url', 42, 'https://app.acme.com'] },
      navigation: {
        drawer: { enabled: true, items: [{ id: 'a', label: 'A', url: 'https://app.acme.com/x' }] },
      },
    };
    expect(originCoverageRule.run(ctx(config))).toEqual([]);
  });
});

describe('asset references in nested positions', () => {
  const ref = 'asset://sha256-0000000000000000000000000000000000000000000000000000000000000000';

  it('finds a reference inside an array', () => {
    const config: JsonObject = { nativeSurfaces: [{ id: 'a', config: { images: [ref] } }] };
    const found = assetExistsRule.run({
      config,
      plugins: builtInPluginRegistry,
      assets: { lookup: () => undefined },
    });
    expect(found[0]?.path).toBe('/nativeSurfaces/0/config/images/0');
  });

  it('leaves a string that merely looks similar alone', () => {
    const config: JsonObject = { webOverrides: { headers: { 'X-A': 'asset://sha256-short' } } };
    const found = assetExistsRule.run({
      config,
      plugins: builtInPluginRegistry,
      assets: { lookup: () => undefined },
    });
    expect(found).toEqual([]);
  });
});

describe('schema error messages', () => {
  it('explains a format failure in plain language', () => {
    const { result } = validate({
      schemaVersion: 1,
      app: {
        name: 'A',
        bundleId: 'com.a.b',
        initialUrl: 'https://a.example.com/',
        allowedOrigins: ['not-an-origin'],
      },
    });
    expect(result.errors[0]?.message).toContain('not in the expected format');
  });

  it('names the missing field when a required one is absent', () => {
    const { result } = validate({ schemaVersion: 1, app: { name: 'A', bundleId: 'com.a.b' } });
    expect(result.errors.some((d) => d.message.includes('initialUrl'))).toBe(true);
  });

  it('names the offending key when a field is not recognised', () => {
    const { result } = validate({
      schemaVersion: 1,
      app: {
        name: 'A',
        bundleId: 'com.a.b',
        initialUrl: 'https://a.example.com/',
        allowedOrigins: ['https://a.example.com'],
        typo: true,
      },
    });
    const unknown = result.errors.find((d) => d.code === DiagnosticCode.UnknownField);
    expect(unknown?.message).toContain('typo');
  });
});

describe('the schema message catalogue', () => {
  /**
   * Both implementations map schema keywords to the same user-facing wording.
   * These cases pin the mapping down so a drift shows up here rather than as a
   * cross-language contract failure with a confusing diff.
   */
  it.each([
    ['boolean', 'either on or off'],
    ['string', 'text'],
    ['number', 'a number'],
    ['integer', 'a whole number'],
    ['array', 'a list'],
    ['object', 'a group of settings'],
    ['null', 'empty'],
    ['unheard-of', 'a valid value'],
  ])('names the %s type as "%s"', (jsonType, expected) => {
    expect(typeName(jsonType)).toBe(expected);
  });

  it.each([
    ['enum', 'must be one of the allowed values'],
    ['const', 'must be one of the allowed values'],
    ['minLength', 'is too short'],
    ['maxLength', 'is too long'],
    ['minimum', 'is below the smallest allowed number'],
    ['exclusiveMinimum', 'is below the smallest allowed number'],
    ['maximum', 'is above the largest allowed number'],
    ['exclusiveMaximum', 'is above the largest allowed number'],
    ['minItems', 'does not have enough entries'],
    ['maxItems', 'has too many entries'],
    ['uniqueItems', 'contains the same entry twice'],
    ['multipleOf', 'is not one of the allowed steps'],
    ['someFutureKeyword', 'is not valid here'],
  ])('describes a failed %s as "%s"', (keyword, expected) => {
    expect(constraint({ keyword, params: {} } as never)).toBe(expected);
  });

  it('reads the expected type from a type failure', () => {
    expect(constraint({ keyword: 'type', params: { type: 'array' } } as never)).toBe(
      'must be a list',
    );
    // Ajv reports a union as an array of type names; the first is enough to
    // describe the failure without listing every alternative.
    expect(constraint({ keyword: 'type', params: { type: ['string', 'object'] } } as never)).toBe(
      'must be text',
    );
    expect(constraint({ keyword: 'type', params: {} } as never)).toBe('must be a valid value');
  });
});
