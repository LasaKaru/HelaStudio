import { describe, expect, it } from 'vitest';
import fc from 'fast-check';
import { canonicalize, canonicalNumber, canonicalString } from '../src/canonical.js';
import type { JsonValue } from '../src/canonical.js';
import { readConfig, validFixtures } from './fixtures.js';

/** An arbitrary producing any JSON value, for the property tests below. */
const jsonValue = fc.letrec<{ value: JsonValue }>((tie) => ({
  value: fc.oneof(
    { depthSize: 'small' },
    fc.constant(null),
    fc.boolean(),
    fc.integer({ min: -1e9, max: 1e9 }),
    fc.string(),
    fc.array(tie('value'), { maxLength: 5 }),
    fc.dictionary(fc.string({ minLength: 1 }), tie('value'), { maxKeys: 5 }),
  ) as fc.Arbitrary<JsonValue>,
})).value;

/** Recursively rebuilds an object with its keys in a different order. */
function shuffleKeys(value: JsonValue): JsonValue {
  if (Array.isArray(value)) return value.map(shuffleKeys);
  if (typeof value !== 'object' || value === null) return value;
  const entries = Object.entries(value).reverse();
  return Object.fromEntries(entries.map(([k, v]) => [k, shuffleKeys(v)]));
}

describe('canonicalize', () => {
  // TC-S01-CFG-035
  it('sorts object keys by UTF-16 code unit', () => {
    expect(canonicalize({ b: 1, a: 2, A: 3 })).toBe('{"A":3,"a":2,"b":1}');
  });

  // TC-S01-CFG-036
  it('omits null-valued keys, so an explicit null and an absent key agree', () => {
    expect(canonicalize({ a: 1, b: null })).toBe(canonicalize({ a: 1 }));
  });

  // TC-S01-CFG-037
  it('preserves array order and array nulls', () => {
    expect(canonicalize([3, null, 1])).toBe('[3,null,1]');
  });

  // TC-S01-CFG-038
  it('is order-independent over generated documents', () => {
    fc.assert(
      fc.property(jsonValue, (value) => {
        expect(canonicalize(shuffleKeys(value))).toBe(canonicalize(value));
      }),
      { numRuns: 1000 },
    );
  });

  // TC-S01-CFG-039
  it('is idempotent through a parse round trip', () => {
    fc.assert(
      fc.property(jsonValue, (value) => {
        const once = canonicalize(value);
        expect(canonicalize(JSON.parse(once) as JsonValue)).toBe(once);
      }),
      { numRuns: 1000 },
    );
  });

  it('produces stable bytes for every valid fixture', () => {
    for (const name of validFixtures) {
      const config = readConfig(name);
      expect(canonicalize(config)).toBe(canonicalize(shuffleKeys(config)));
    }
  });
});

describe('canonicalNumber', () => {
  // TC-S01-CFG-040
  it('uses shortest round-trip form', () => {
    expect(canonicalNumber(1.0)).toBe('1');
    expect(canonicalNumber(-0)).toBe('0');
    expect(canonicalNumber(1.5)).toBe('1.5');
    expect(canonicalNumber(1e21)).toBe('1E21');
    expect(canonicalNumber(1e-7)).toBe('1E-7');
  });

  it('rejects non-finite numbers rather than emitting invalid JSON', () => {
    expect(() => canonicalNumber(Number.NaN)).toThrow(RangeError);
    expect(() => canonicalNumber(Number.POSITIVE_INFINITY)).toThrow(RangeError);
  });
});

describe('canonicalString', () => {
  // TC-S01-CFG-041
  it('normalises to NFC, so composed and decomposed forms agree', () => {
    const composed = '\u00e9';
    const decomposed = 'e\u0301';
    expect(canonicalString(decomposed)).toBe(canonicalString(composed));
  });

  it('escapes quotes, backslashes, and control characters', () => {
    expect(canonicalString('a"b\\c\nd\u0001')).toBe('"a\\"b\\\\c\\nd\\u0001"');
  });

  it('leaves emoji and non-Latin scripts unescaped', () => {
    const label = '\u{1F3E0} \u0627\u0644\u0631\u0626\u064A\u0633\u064A\u0629';
    expect(canonicalString(label)).toBe(`"${label}"`);
  });
});
