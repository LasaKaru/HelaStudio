/**
 * Performance budgets from 03_TEST_STRATEGY.md §12.
 *
 * These are asserted, not aspirational. A budget is raised only by an explicit,
 * justified decision recorded in the pull request — never by quietly editing the
 * number here.
 *
 * Each measurement is the median of repeated runs, which keeps the assertion
 * stable on a noisy shared CI runner without inflating the budget itself.
 */
import { describe, expect, it } from 'vitest';
import { computeHashes } from '../src/hash.js';
import { validate } from '../src/validate.js';
import { readConfig } from './fixtures.js';

/** Median wall-clock milliseconds over `runs` iterations, after a warm-up. */
function medianMs(runs: number, work: () => void): number {
  for (let i = 0; i < 5; i++) work();
  const samples: number[] = [];
  for (let i = 0; i < runs; i++) {
    const started = performance.now();
    work();
    samples.push(performance.now() - started);
  }
  samples.sort((a, b) => a - b);
  return samples[Math.floor(samples.length / 2)] as number;
}

describe('performance budgets', () => {
  // TC-S01-PRF-001 — validation runs on every keystroke in the studio, debounced
  // to 300 ms. Anything near that budget would make typing feel laggy.
  it('validates the maximal fixture in under 50 ms', () => {
    const config = readConfig('maximal.json');
    const elapsed = medianMs(50, () => {
      validate(config);
    });
    expect(elapsed).toBeLessThan(50);
  });

  // TC-S01-PRF-002 — hashing gates every build, so it sits on the hot path.
  it('hashes the maximal fixture in under 5 ms', () => {
    const { resolved } = validate(readConfig('maximal.json'));
    const context = { shellVersion: '1.0.0' };
    const elapsed = medianMs(100, () => {
      computeHashes(resolved, context);
    });
    expect(elapsed).toBeLessThan(5);
  });

  // TC-S01-PRF-003 — 200 link rules is the documented upper bound for a real
  // customer, and each one carries a regex that must be safety-checked.
  it('validates 200 link rules in under 50 ms', () => {
    const config = readConfig('edge-many-linkrules.json');
    const elapsed = medianMs(20, () => {
      validate(config);
    });
    expect(elapsed).toBeLessThan(50);
  });

  // TC-S01-PRF-004 — the backtracking checker must never itself become the hang
  // it exists to prevent.
  it('rejects a catastrophic pattern without hanging', () => {
    const config = readConfig('minimal.json');
    config['linkRules'] = [
      { id: 'a', pattern: `^(${'a+'.repeat(50)})+$`, action: 'internal' },
      { id: 'b', pattern: '.*', action: 'externalBrowser' },
    ];
    const started = performance.now();
    const { result } = validate(config);
    expect(performance.now() - started).toBeLessThan(50);
    expect(result.valid).toBe(false);
  });
});
