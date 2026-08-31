import { describe, expect, it } from 'vitest';
import fc from 'fast-check';
import { computeHashes, hashValue, type HashContext } from '../src/hash.js';
import { validate } from '../src/validate.js';
import type { JsonObject } from '../src/canonical.js';
import { readConfig, validFixtures } from './fixtures.js';

const context: HashContext = {
  shellVersion: '1.0.0',
  toolchain: { xcode: '26.1', agp: '8.9' },
  pluginLock: { 'qr-scanner': '1.2.0' },
};

/** Resolves a fixture through validation so defaults are filled in before hashing. */
function resolvedFixture(name: string): JsonObject {
  return validate(readConfig(name)).resolved;
}

/** Applies a mutation to a deep copy of a resolved fixture. */
function mutate(name: string, change: (config: JsonObject) => void): JsonObject {
  const copy = structuredClone(resolvedFixture(name));
  change(copy);
  return copy;
}

describe('computeHashes', () => {
  // TC-S01-CFG-042
  it('is stable across repeated runs', () => {
    const config = resolvedFixture('maximal.json');
    const first = computeHashes(config, context);
    for (let i = 0; i < 10; i++) {
      expect(computeHashes(config, context)).toEqual(first);
    }
  });

  it('produces 64-character hex keys', () => {
    const hashes = computeHashes(resolvedFixture('maximal.json'), context);
    for (const key of Object.values(hashes)) {
      expect(key).toMatch(/^[0-9a-f]{64}$/);
    }
  });

  // TC-S01-CFG-043: an omitted field and an explicitly-default field must agree,
  // or a user toggling a value back to its default would miss the build cache.
  it('hashes an omitted default the same as an explicit one', () => {
    const implicit = resolvedFixture('minimal.json');

    const raw = readConfig('minimal.json');
    raw['branding'] = { darkMode: 'system' };
    (raw['app'] as JsonObject)['versionName'] = '1.0.0';
    const explicit = validate(raw).resolved;

    expect(computeHashes(explicit, context)).toEqual(computeHashes(implicit, context));
  });
});

describe('the hash split', () => {
  const baseline = computeHashes(resolvedFixture('maximal.json'), context);

  // TC-S01-CFG-044
  it('a branding change moves only the asset key', () => {
    const changed = mutate('maximal.json', (c) => {
      ((c['branding'] as JsonObject)['theme'] as JsonObject)['primary'] = '#FF0000';
    });
    const hashes = computeHashes(changed, context);
    expect(hashes.assetKey).not.toBe(baseline.assetKey);
    expect(hashes.codeKey).toBe(baseline.codeKey);
    expect(hashes.contentKey).toBe(baseline.contentKey);
  });

  // TC-S01-CFG-045
  it('a tab label change moves only the asset key', () => {
    const changed = mutate('maximal.json', (c) => {
      const nav = c['navigation'] as JsonObject;
      const items = (nav['tabBar'] as JsonObject)['items'] as JsonObject[];
      (items[0] as JsonObject)['label'] = 'Start';
    });
    const hashes = computeHashes(changed, context);
    expect(hashes.assetKey).not.toBe(baseline.assetKey);
    expect(hashes.codeKey).toBe(baseline.codeKey);
    expect(hashes.contentKey).toBe(baseline.contentKey);
  });

  // TC-S01-CFG-046
  it('a plugin change moves the code key', () => {
    const changed = mutate('maximal.json', (c) => {
      delete (c['plugins'] as JsonObject)['haptics'];
    });
    const hashes = computeHashes(changed, context);
    expect(hashes.codeKey).not.toBe(baseline.codeKey);
    expect(hashes.assetKey).toBe(baseline.assetKey);
  });

  // TC-S01-CFG-047
  it('a link rule change moves only the content key', () => {
    const changed = mutate('maximal.json', (c) => {
      const rules = c['linkRules'] as JsonObject[];
      (rules[0] as JsonObject)['action'] = 'modal';
    });
    const hashes = computeHashes(changed, context);
    expect(hashes.contentKey).not.toBe(baseline.contentKey);
    expect(hashes.codeKey).toBe(baseline.codeKey);
    expect(hashes.assetKey).toBe(baseline.assetKey);
  });

  // TC-S01-CFG-048
  it('a start URL change moves only the content key', () => {
    const changed = mutate('maximal.json', (c) => {
      (c['app'] as JsonObject)['initialUrl'] = 'https://app.acme.com/home';
    });
    const hashes = computeHashes(changed, context);
    expect(hashes.contentKey).not.toBe(baseline.contentKey);
    expect(hashes.codeKey).toBe(baseline.codeKey);
    expect(hashes.assetKey).toBe(baseline.assetKey);
  });

  it('a permission change moves the code key, since it rewrites the manifest', () => {
    const changed = mutate('maximal.json', (c) => {
      (c['permissions'] as JsonObject)['contacts'] = true;
    });
    expect(computeHashes(changed, context).codeKey).not.toBe(baseline.codeKey);
  });

  it('a shell version bump moves the code key alone', () => {
    const config = resolvedFixture('maximal.json');
    const hashes = computeHashes(config, { ...context, shellVersion: '1.1.0' });
    expect(hashes.codeKey).not.toBe(baseline.codeKey);
    expect(hashes.assetKey).toBe(baseline.assetKey);
    expect(hashes.contentKey).toBe(baseline.contentKey);
  });

  it('every valid fixture produces three distinct keys', () => {
    for (const name of validFixtures) {
      const hashes = computeHashes(resolvedFixture(name), context);
      expect(new Set(Object.values(hashes)).size).toBe(3);
    }
  });
});

describe('hashValue', () => {
  it('agrees with itself regardless of key order', () => {
    fc.assert(
      fc.property(
        fc.dictionary(fc.string({ minLength: 1 }), fc.integer(), { minKeys: 1, maxKeys: 8 }),
        (record) => {
          const reversed = Object.fromEntries(Object.entries(record).reverse());
          expect(hashValue(reversed)).toBe(hashValue(record));
        },
      ),
      { numRuns: 500 },
    );
  });
});
