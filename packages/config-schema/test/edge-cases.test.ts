/**
 * Branch coverage for the defensive paths.
 *
 * Every input reaching this package is untrusted — a hand-written config, a
 * partially-typed document from the studio, or a stored config from an older
 * release. These tests pin down what happens on the malformed shapes that the
 * happy-path corpus never produces.
 */
import { describe, expect, it } from 'vitest';
import { canonicalize } from '../src/canonical.js';
import { resolveDefaults } from '../src/defaults.js';
import { pointer, pointerSegment, toResult } from '../src/diagnostics.js';
import { computeHashes, hashValue } from '../src/hash.js';
import { checkRegex } from '../src/rules/regex-safety.js';
import { appConfigSchema, validate } from '../src/validate.js';
import type { JsonObject } from '../src/canonical.js';

describe('checkRegex — structural edge cases', () => {
  it.each([
    // Bounded quantifiers cannot explode, so they are left alone.
    ['(a{2})+', 'ok'],
    ['(a{2,4})+', 'ok'],
    // An open-ended {n,} inside a repetition can.
    ['(a{2,})+', 'catastrophic'],
    // A lazy or possessive outer quantifier bounds the search.
    ['(a+)+?', 'ok'],
    // An optional group is tried at most once.
    ['(a*)?', 'ok'],
    // Non-capturing and lookaround prefixes are stripped before analysis.
    ['(?:a+)+', 'catastrophic'],
    ['(?<name>a+)+', 'catastrophic'],
    ['(?=a+)+', 'catastrophic'],
    // A character class atom is one atom, quantified or not.
    ['([a-z]+)+', 'catastrophic'],
    ['([a-z]x)+', 'ok'],
    // An escaped atom likewise.
    ['(\\d+)+', 'catastrophic'],
    ['(\\d)+', 'ok'],
    // Alternation overlaps only when a branch is a prefix of another.
    ['(a|ab)*', 'catastrophic'],
    ['(-a|-b)*', 'ok'],
    ['(cat|dog|bird)*', 'ok'],
    // A pipe inside a nested group or class is not a top-level alternation.
    ['(x[a|b]y)*', 'ok'],
    // A group that never closes cannot be analysed, and must not throw.
    ['(a+', 'invalid'],
    // An unterminated class likewise.
    ['([a+)+', 'invalid'],
    // Escaped parentheses are literals, not groups.
    ['\\(a+\\)+', 'ok'],
    // Anchors consume nothing, so they open no atom.
    ['(^a)+', 'ok'],
    ['', 'ok'],
  ])('classifies %s as %s', (pattern, kind) => {
    expect(checkRegex(pattern).kind).toBe(kind);
  });

  it('accepts a pattern that is valid only outside Unicode mode', () => {
    // \p means "literal p" without the u flag, but is an error with it.
    expect(checkRegex('\\p').kind).toBe('ok');
  });
});

describe('computeHashes — degenerate documents', () => {
  const context = { shellVersion: '1.0.0' };

  it('hashes an empty document without throwing', () => {
    const hashes = computeHashes({}, context);
    expect(hashes.codeKey).toMatch(/^[0-9a-f]{64}$/);
  });

  it('tolerates fields of the wrong JSON type', () => {
    const broken: JsonObject = {
      app: 'not an object',
      navigation: [1, 2, 3],
      nativeSurfaces: 'not an array',
      plugins: null,
    };
    expect(() => computeHashes(broken, context)).not.toThrow();
  });

  it('treats an absent plugin lock and toolchain as distinct from an empty one', () => {
    const config: JsonObject = { app: { bundleId: 'com.acme.app' } };
    const bare = computeHashes(config, context);
    const withLock = computeHashes(config, { ...context, pluginLock: {} });
    expect(bare.codeKey).not.toBe(withLock.codeKey);
  });

  it('hashes scalars and arrays at the top level', () => {
    expect(hashValue(null)).toMatch(/^[0-9a-f]{64}$/);
    expect(hashValue([1, 'a', true])).toMatch(/^[0-9a-f]{64}$/);
  });
});

describe('resolveDefaults — unusual inputs', () => {
  it('leaves a non-object document alone', () => {
    expect(resolveDefaults('a string', appConfigSchema)).toBe('a string');
    expect(resolveDefaults([1, 2], appConfigSchema)).toEqual([1, 2]);
  });

  it('does not materialise a nested optional object that has no defaults', () => {
    const { resolved } = validate({
      schemaVersion: 1,
      app: {
        name: 'A',
        bundleId: 'com.a.b',
        initialUrl: 'https://a.example.com/',
        allowedOrigins: ['https://a.example.com'],
      },
    });
    // `branding.splash.dark` carries no defaults of its own, so it stays absent
    // rather than becoming an empty object that would pollute the asset hash.
    const splash = (resolved['branding'] as JsonObject)['splash'] as JsonObject;
    expect(splash['dark']).toBeUndefined();
    // `deepLinks`, by contrast, does have a default and so is materialised.
    expect(resolved['deepLinks']).toEqual({ universalLinks: [] });
  });

  it('fills defaults into every element of an array of objects', () => {
    const { resolved } = validate({
      schemaVersion: 1,
      app: {
        name: 'A',
        bundleId: 'com.a.b',
        initialUrl: 'https://a.example.com/',
        allowedOrigins: ['https://a.example.com'],
      },
      nativeSurfaces: [
        { id: 'one', type: 'onboarding' },
        { id: 'two', type: 'settings' },
      ],
    });
    const surfaces = resolved['nativeSurfaces'] as JsonObject[];
    expect(surfaces.every((s) => s['showOnce'] === false && s['config'] !== undefined)).toBe(true);
  });

  it('throws on an unresolvable schema reference rather than silently skipping', () => {
    const broken = { properties: { a: { $ref: '#/$defs/Missing' } }, $defs: {} };
    expect(() => resolveDefaults({ a: 1 }, broken as unknown as JsonObject)).toThrow(
      /Unresolvable schema reference/,
    );
  });
});

describe('diagnostics helpers', () => {
  it('escapes JSON Pointer segments per RFC 6901', () => {
    expect(pointerSegment('a/b')).toBe('a~1b');
    expect(pointerSegment('a~b')).toBe('a~0b');
    expect(pointerSegment(3)).toBe('3');
    expect(pointer('plugins', 'qr-scanner')).toBe('/plugins/qr-scanner');
    expect(pointer()).toBe('');
  });

  it('reports an empty result as valid', () => {
    const result = toResult([]);
    expect(result).toEqual({ valid: true, errors: [], warnings: [], info: [] });
  });
});

describe('canonicalize — nesting', () => {
  it('handles the deeply nested fixture without recursion trouble', () => {
    let node: JsonObject = { leaf: true };
    for (let i = 0; i < 200; i++) node = { nested: node };
    expect(canonicalize(node).length).toBeGreaterThan(200);
  });
});
