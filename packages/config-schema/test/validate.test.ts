import { describe, expect, it } from 'vitest';
import { validate } from '../src/validate.js';
import { DiagnosticCode } from '../src/diagnostics.js';
import type { AssetResolver, RuleContext } from '../src/rules/rule.js';
import { assetExistsRule, iconAlphaRule, iconDimensionsRule } from '../src/rules/asset-rules.js';
import { declaredCode, listConfigs, readConfig, validFixtures } from './fixtures.js';

/** Distinct diagnostic codes across every severity. */
function allCodes(result: {
  errors: readonly { code: string }[];
  warnings: readonly { code: string }[];
}) {
  return new Set([...result.errors, ...result.warnings].map((d) => d.code));
}

describe('validate — the valid corpus', () => {
  // TC-S01-CFG-001
  it.each(validFixtures)('%s validates with no errors', (name) => {
    const { result } = validate(readConfig(name));
    expect(result.errors, JSON.stringify(result.errors, null, 2)).toEqual([]);
    expect(result.valid).toBe(true);
  });

  // TC-S01-CFG-002
  it('resolves schema defaults into the returned document', () => {
    const { resolved } = validate(readConfig('minimal.json'));
    const app = resolved['app'] as Record<string, unknown>;
    expect(app['versionName']).toBe('1.0.0');
    expect(app['versionCode']).toBe(1);
    expect((resolved['branding'] as Record<string, unknown>)['darkMode']).toBe('system');
    expect(
      (resolved['build'] as Record<string, Record<string, unknown>>)['android']?.['minSdk'],
    ).toBe(24);
  });

  // TC-S01-CFG-003
  it('preserves x- extension objects untouched', () => {
    const { resolved } = validate(readConfig('maximal.json'));
    expect(resolved['x-acme']).toEqual({ internalTicket: 'ACME-1421' });
  });
});

describe('validate — the invalid corpus', () => {
  // TC-S01-CFG-049: each invalid-*.json yields exactly the code its name declares.
  it.each(listConfigs('invalid-'))('%s yields exactly its declared code', (name) => {
    const expected = declaredCode(name);
    const { result } = validate(readConfig(name));
    const codes = allCodes(result);
    expect([...codes], JSON.stringify([...result.errors, ...result.warnings], null, 2)).toEqual([
      expected,
    ]);
  });

  // TC-S01-CFG-004
  it('rejects an uppercase bundle id with actionable text', () => {
    const { result } = validate(readConfig('invalid-CFG_BUNDLE_ID_INVALID.json'));
    const error = result.errors[0];
    expect(error?.code).toBe(DiagnosticCode.BundleIdInvalid);
    expect(error?.path).toBe('/app/bundleId');
    expect(error?.message).toContain('com.acme.app');
  });

  // TC-S01-CFG-005
  it('short-circuits semantic rules when the shape is wrong', () => {
    // A document failing the schema produces schema diagnostics only, never a
    // cascade of secondary rule failures against a malformed shape.
    const { result } = validate({ schemaVersion: 1, app: { name: 'x' } });
    expect(result.valid).toBe(false);
    expect([...allCodes(result)]).toEqual([DiagnosticCode.SchemaViolation]);
  });

  // TC-S01-CFG-006
  it('refuses a configuration from a future schema version', () => {
    const { result } = validate({ schemaVersion: 9, app: {} });
    expect(result.errors[0]?.code).toBe(DiagnosticCode.SchemaVersionUnsupported);
  });
});

describe('validate — determinism', () => {
  // TC-S01-CFG-007
  it('produces byte-identical diagnostics across repeated runs', () => {
    const config = readConfig('invalid-CFG_PLUGIN_CONFLICT.json');
    const first = JSON.stringify(validate(config).result);
    for (let i = 0; i < 20; i++) {
      expect(JSON.stringify(validate(config).result)).toBe(first);
    }
  });

  // TC-S01-CFG-008
  it('sorts diagnostics by path then code', () => {
    const config = readConfig('minimal.json');
    config['linkRules'] = [
      { id: 'b', pattern: '^(a+)+$', action: 'internal' },
      { id: 'a', pattern: '^(b+)+$', action: 'internal' },
    ];
    const { result } = validate(config);
    const paths = result.errors.map((d) => d.path);
    expect(paths).toEqual([...paths].sort((x, y) => x.localeCompare(y)));
  });
});

describe('validate — warnings do not block', () => {
  // TC-S01-CFG-009
  it('warns about more than five tabs without failing validation', () => {
    const { result } = validate(readConfig('edge-many-tabs.json'));
    expect(result.valid).toBe(true);
    expect(result.warnings.map((d) => d.code)).toContain(DiagnosticCode.TabCountHigh);
  });

  // TC-S01-CFG-010
  it('warns that a config with no native features risks a 4.2 rejection', () => {
    const { result } = validate(readConfig('edge-single-page.json'));
    expect(result.valid).toBe(true);
    expect(result.warnings[0]?.code).toBe(DiagnosticCode.NoNativeFeatures);
    expect(result.warnings[0]?.message).toContain('4.2');
  });
});

describe('asset rules', () => {
  const iconRef = 'asset://sha256-1111111111111111111111111111111111111111111111111111111111111111';

  function contextWith(assets: AssetResolver | undefined): RuleContext {
    return {
      config: { branding: { icon: iconRef } },
      plugins: { get: () => undefined },
      assets,
    };
  }

  // TC-S01-CFG-030
  it('skips entirely when there is no asset store to consult', () => {
    const context = contextWith(undefined);
    expect(assetExistsRule.run(context)).toEqual([]);
    expect(iconDimensionsRule.run(context)).toEqual([]);
    expect(iconAlphaRule.run(context)).toEqual([]);
  });

  // TC-S01-CFG-031
  it('reports an asset that was never uploaded', () => {
    const found = assetExistsRule.run(contextWith({ lookup: () => undefined }));
    expect(found[0]?.code).toBe(DiagnosticCode.AssetMissing);
  });

  // TC-S01-CFG-032
  it('rejects an icon that is too small or not square', () => {
    const small = { lookup: () => ({ width: 512, height: 512, hasAlpha: false }) };
    expect(iconDimensionsRule.run(contextWith(small))[0]?.code).toBe(DiagnosticCode.IconDimensions);

    const oblong = { lookup: () => ({ width: 1024, height: 768, hasAlpha: false }) };
    expect(iconDimensionsRule.run(contextWith(oblong))[0]?.code).toBe(
      DiagnosticCode.IconDimensions,
    );

    const good = { lookup: () => ({ width: 1024, height: 1024, hasAlpha: false }) };
    expect(iconDimensionsRule.run(contextWith(good))).toEqual([]);
  });

  // TC-S01-CFG-033
  it('rejects an icon with an alpha channel, which Apple will not accept', () => {
    const alpha = { lookup: () => ({ width: 1024, height: 1024, hasAlpha: true }) };
    const found = iconAlphaRule.run(contextWith(alpha));
    expect(found[0]?.code).toBe(DiagnosticCode.IconAlpha);
    expect(found[0]?.message).toContain('alpha');
  });
});
