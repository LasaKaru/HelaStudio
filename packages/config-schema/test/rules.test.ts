/**
 * One passing and one failing case per semantic rule.
 *
 * Rules are tested directly rather than through `validate` so a failure names
 * the rule, not the corpus.
 */
import { describe, expect, it } from 'vitest';
import { DiagnosticCode } from '../src/diagnostics.js';
import { builtInPluginRegistry } from '../src/plugin-registry.js';
import { checkRegex } from '../src/rules/regex-safety.js';
import { catchAllRule, regexSafetyRule, unreachableRuleRule } from '../src/rules/link-rules.js';
import {
  pluginConfigRule,
  pluginConflictRule,
  pluginKnownRule,
  pluginPermissionRule,
  pluginPlatformFloorRule,
} from '../src/rules/plugin-rules.js';
import { noSecretsRule } from '../src/rules/secret-rules.js';
import {
  duplicateIdRule,
  nativeFeaturesRule,
  permissionJustifiedRule,
  tabCountRule,
} from '../src/rules/store-readiness.js';
import {
  cleartextUrlRule,
  initialUrlAllowedRule,
  originCoverageRule,
} from '../src/rules/url-rules.js';
import type { JsonObject } from '../src/canonical.js';
import type { RuleContext, ValidationRule } from '../src/rules/rule.js';

function ctx(config: JsonObject): RuleContext {
  return { config, plugins: builtInPluginRegistry };
}

/** Asserts a rule is silent on `passing` and reports `code` on `failing`. */
function expectRule(
  rule: ValidationRule,
  code: string,
  passing: JsonObject,
  failing: JsonObject,
): void {
  expect(rule.run(ctx(passing)), `${rule.name} should be silent on the passing case`).toEqual([]);
  const found = rule.run(ctx(failing));
  expect(found.length, `${rule.name} should report on the failing case`).toBeGreaterThan(0);
  expect(found[0]?.code).toBe(code);
}

const APP = {
  name: 'Acme',
  bundleId: 'com.acme.app',
  initialUrl: 'https://app.acme.com/',
  allowedOrigins: ['https://app.acme.com'],
};

describe('url rules', () => {
  // TC-S01-CFG-013 / 014
  it('initial-url-allowed', () => {
    expectRule(
      initialUrlAllowedRule,
      DiagnosticCode.InitialUrlNotAllowed,
      { app: APP },
      { app: { ...APP, allowedOrigins: ['https://other.example.com'] } },
    );
  });

  // TC-S01-CFG-015 / 016
  it('origin-coverage', () => {
    const drawer = (url: string): JsonObject => ({
      app: APP,
      navigation: { drawer: { enabled: true, items: [{ id: 'a', label: 'A', url }] } },
    });
    expectRule(
      originCoverageRule,
      DiagnosticCode.OriginNotCovered,
      drawer('https://app.acme.com/x'),
      drawer('https://partner.example.com/x'),
    );
  });

  it('origin-coverage treats a bare path as covered by definition', () => {
    const config: JsonObject = {
      app: APP,
      navigation: { drawer: { enabled: true, items: [{ id: 'a', label: 'A', url: '/orders' }] } },
    };
    expect(originCoverageRule.run(ctx(config))).toEqual([]);
  });

  // TC-S01-CFG-017 / 018
  it('cleartext-url', () => {
    expectRule(
      cleartextUrlRule,
      DiagnosticCode.CleartextUrl,
      { app: APP },
      { app: APP, nativeSurfaces: [{ id: 'a', type: 'about', config: { u: 'http://acme.com' } }] },
    );
  });

  it('cleartext-url ignores x- extension blocks', () => {
    expect(cleartextUrlRule.run(ctx({ 'x-legacy': { url: 'http://old.example.com' } }))).toEqual(
      [],
    );
  });
});

describe('link rules', () => {
  const withRules = (pattern: string): JsonObject => ({
    linkRules: [{ id: 'a', pattern, action: 'internal' }],
  });

  // TC-S01-CFG-019 / 020
  it('regex-safety rejects an uncompilable pattern', () => {
    expectRule(
      regexSafetyRule,
      DiagnosticCode.RegexInvalid,
      withRules('^https://app\\.acme\\.com'),
      withRules('^https://app\\.acme\\.com('),
    );
  });

  // TC-S01-CFG-021: catastrophic backtracking would freeze the shell on every
  // navigation, so the checker must reject it — and must not hang doing so.
  it('regex-safety rejects nested quantifiers quickly', () => {
    const started = performance.now();
    const found = regexSafetyRule.run(ctx(withRules('^(a+)+$')));
    expect(performance.now() - started).toBeLessThan(50);
    expect(found[0]?.code).toBe(DiagnosticCode.RegexCatastrophic);
    expect(found[0]?.path).toBe('/linkRules/0/pattern');
    expect(found[0]?.message).toContain('(a+)+');
  });

  it.each([
    ['^(a+)+$', 'catastrophic'],
    ['^(a*)*$', 'catastrophic'],
    ['(a|a)*$', 'catastrophic'],
    ['^(x+x+)+y$', 'catastrophic'],
    ['^https://app\\.acme\\.com', 'ok'],
    ['.*', 'ok'],
    ['^/orders/[0-9]+$', 'ok'],
    ['(abc)+', 'ok'],
    ['(a+)?', 'ok'],
    ['^[a-z]+(-[a-z]+)*$', 'ok'],
    ['(', 'invalid'],
    ['[z-a]', 'invalid'],
  ])('checkRegex classifies %s as %s', (pattern, kind) => {
    expect(checkRegex(pattern).kind).toBe(kind);
  });

  // TC-S01-CFG-022 / 023
  it('link-rule-unreachable', () => {
    expectRule(
      unreachableRuleRule,
      DiagnosticCode.LinkRuleUnreachable,
      {
        linkRules: [
          { id: 'a', pattern: '^https://app\\.acme\\.com/orders', action: 'internal' },
          { id: 'b', pattern: '.*', action: 'externalBrowser' },
        ],
      },
      {
        linkRules: [
          { id: 'a', pattern: '.*', action: 'internal' },
          { id: 'b', pattern: '^https://help\\.acme\\.com', action: 'modal' },
        ],
      },
    );
  });

  // TC-S01-CFG-024 / 025
  it('link-rule-catchall', () => {
    expectRule(
      catchAllRule,
      DiagnosticCode.LinkRuleNoCatchall,
      { linkRules: [{ id: 'a', pattern: '.*', action: 'externalBrowser' }] },
      { linkRules: [{ id: 'a', pattern: '^https://app\\.acme\\.com', action: 'internal' }] },
    );
  });

  it('link-rule-catchall stays silent when there are no rules at all', () => {
    expect(catchAllRule.run(ctx({}))).toEqual([]);
  });
});

describe('store readiness rules', () => {
  const tabs = (count: number): JsonObject => ({
    navigation: {
      tabBar: {
        enabled: true,
        items: Array.from({ length: count }, (_, i) => ({
          id: `t${String(i)}`,
          label: `T${String(i)}`,
          url: `/${String(i)}`,
        })),
      },
    },
  });

  // TC-S01-CFG-026 / 027
  it('tab-count', () => {
    expectRule(tabCountRule, DiagnosticCode.TabCountHigh, tabs(5), tabs(8));
  });

  // TC-S01-CFG-028 / 029
  it('native-features', () => {
    expectRule(nativeFeaturesRule, DiagnosticCode.NoNativeFeatures, tabs(3), { app: APP });
  });

  it.each([
    [{ plugins: { haptics: {} } }],
    [{ nativeSurfaces: [{ id: 'a', type: 'onboarding' }] }],
    [{ deepLinks: { universalLinks: ['app.acme.com'] } }],
    [{ navigation: { drawer: { enabled: true, items: [{ id: 'a', label: 'A' }] } } }],
  ])('native-features accepts %j as a native feature', (config) => {
    expect(nativeFeaturesRule.run(ctx(config as JsonObject))).toEqual([]);
  });

  // TC-S01-CFG-034 / 035
  it('permission-justified', () => {
    expectRule(
      permissionJustifiedRule,
      DiagnosticCode.PermissionUnjustified,
      { permissions: { camera: true }, plugins: { 'qr-scanner': {} } },
      { permissions: { camera: true }, plugins: {} },
    );
  });

  it('permission-justified ignores a permission that is switched off', () => {
    expect(
      permissionJustifiedRule.run(ctx({ permissions: { camera: false, location: 'none' } })),
    ).toEqual([]);
  });

  it('duplicate-id', () => {
    expectRule(
      duplicateIdRule,
      DiagnosticCode.DuplicateItemId,
      { linkRules: [{ id: 'a', pattern: '.*', action: 'internal' }] },
      {
        linkRules: [
          { id: 'a', pattern: '^/x', action: 'internal' },
          { id: 'a', pattern: '.*', action: 'internal' },
        ],
      },
    );
  });
});

describe('plugin rules', () => {
  it('plugin-known', () => {
    expectRule(
      pluginKnownRule,
      DiagnosticCode.PluginUnknown,
      { plugins: { haptics: {} } },
      { plugins: { telepathy: {} } },
    );
  });

  it('plugin-config', () => {
    expectRule(
      pluginConfigRule,
      DiagnosticCode.PluginConfigInvalid,
      { plugins: { 'qr-scanner': { beepOnScan: true } } },
      { plugins: { 'qr-scanner': { beepOnScan: 'yes' } } },
    );
  });

  it('plugin-conflict', () => {
    expectRule(
      pluginConflictRule,
      DiagnosticCode.PluginConflict,
      { plugins: { 'qr-scanner': {} } },
      { plugins: { 'qr-scanner': {}, 'scandit-scanner': {} } },
    );
  });

  it('plugin-conflict reports each pair only once', () => {
    const found = pluginConflictRule.run(
      ctx({ plugins: { 'qr-scanner': {}, 'scandit-scanner': {} } }),
    );
    expect(found).toHaveLength(1);
  });

  it('plugin-platform-floor', () => {
    expectRule(
      pluginPlatformFloorRule,
      DiagnosticCode.PluginMinSdk,
      {
        plugins: { 'document-scanner': {} },
        build: { android: { minSdk: 26 }, ios: { minVersion: '16.0' } },
      },
      {
        plugins: { 'document-scanner': {} },
        build: { android: { minSdk: 24 }, ios: { minVersion: '16.0' } },
      },
    );
  });

  it('plugin-platform-floor names the iOS floor too', () => {
    const found = pluginPlatformFloorRule.run(
      ctx({ plugins: { 'document-scanner': {} }, build: { android: { minSdk: 26 } } }),
    );
    expect(found[0]?.message).toContain('iOS 16.0');
  });

  it('plugin-permission', () => {
    expectRule(
      pluginPermissionRule,
      DiagnosticCode.PluginPermissionMissing,
      { plugins: { biometric: {} }, permissions: { biometric: true } },
      { plugins: { biometric: {} }, permissions: { biometric: false } },
    );
  });
});

describe('secret rules', () => {
  it.each([
    ['an AWS key', { webOverrides: { headers: { 'X-Key': 'AKIAIOSFODNN7EXAMPLE' } } }],
    ['a Stripe key', { webOverrides: { headers: { 'X-K': 'sk_live_abcdefghijklmnop1234' } } }],
    [
      'a GitHub token',
      { webOverrides: { headers: { 'X-K': 'ghp_abcdefghijklmnopqrstuvwxyz0123456789' } } },
    ],
    [
      'a Google API key',
      { webOverrides: { headers: { 'X-K': 'AIzaSyA1234567890abcdefghijklmnopqrstuv' } } },
    ],
    [
      'a bearer under an auth header',
      { webOverrides: { headers: { Authorization: 'Bearer abc12345' } } },
    ],
  ])('rejects %s', (_label, config) => {
    const found = noSecretsRule.run(ctx(config as JsonObject));
    expect(found[0]?.code).toBe(DiagnosticCode.SecretInConfig);
  });

  it('accepts ordinary header values', () => {
    const config = { webOverrides: { headers: { 'X-Client': 'mobile-app' } } };
    expect(noSecretsRule.run(ctx(config as JsonObject))).toEqual([]);
  });
});
