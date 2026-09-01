/**
 * The backtracking-heuristic contract, shared with the C# validator and both
 * shells.
 *
 * Four implementations of one judgement, sharing no code. If they disagree, a
 * customer's rule either stops working silently or freezes their app. See
 * `tests/fixtures/regex-safety/README.md` for what is deliberately left out.
 */
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { checkRegex } from '../src/rules/regex-safety.js';
import { fixtureRoot } from './fixtures.js';

interface Corpus {
  readonly cases: readonly { pattern: string; verdict: string; why: string }[];
}

const corpus = JSON.parse(
  readFileSync(join(fixtureRoot, 'regex-safety', 'patterns.json'), 'utf8'),
) as Corpus;

describe('regex-safety shared corpus', () => {
  it.each(corpus.cases.map((c) => [c.pattern, c.verdict, c.why] as const))(
    'classifies /%s/ as %s',
    (pattern, verdict, why) => {
      expect(checkRegex(pattern).kind, why).toBe(verdict);
    },
  );

  it('covers all three verdicts', () => {
    // A verdict with no case in the corpus is one the other three
    // implementations are not held to at all.
    const covered = new Set(corpus.cases.map((c) => c.verdict));
    expect([...covered].sort()).toEqual(['catastrophic', 'invalid', 'ok']);
  });
});
