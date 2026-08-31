# Sprint 01 review — Configuration schema and validation engine

**Goal:** define `appconfig.json` v1 and build the validation, canonicalisation,
migration, and hashing machinery around it.

## Exit criteria

| Criterion                                                                     | Status                                                          |
| ----------------------------------------------------------------------------- | --------------------------------------------------------------- |
| JSON Schema 2020-12 for `appconfig` v1 published                              | ✅ 30 definitions, every property carries user-facing help text |
| Validation runs identically in C# and TypeScript, byte-identical output       | ✅ enforced by a golden-file contract test over all 29 fixtures |
| Canonical JSON produces stable bytes; property test proves order-independence | ✅ 1,000 generated cases per invariant                          |
| `codeKey` / `assetKey` / `contentKey` split implemented and tested            | ✅ each key proven to move only for its own inputs              |
| Migration framework with a proven v0→v1 migration and round-trip test         | ✅ reversible, golden-file verified, identity-stable            |
| Fixture corpus: minimal, maximal, all-plugins, unicode, ≥ 6 edge, invalid-\*  | ✅ 29 fixtures                                                  |
| Validation of the maximal fixture in under 50 ms                              | ✅ **0.5 ms**                                                   |
| Coverage ≥ 95% line / 90% branch                                              | ✅ **99.3% / 92.9%**                                            |

## What shipped

Nineteen semantic rules, each in its own file with its own tests. They catch what
JSON Schema structurally cannot: a navigation link that would silently bounce the
user out to their browser, a permission nothing justifies, an icon with an alpha
channel, a plugin needing a newer platform than the app targets, and a credential
pasted into a document that gets embedded in the shipped binary.

Every message names the fix rather than the problem. "Build failed" is a bug;
`"scandit-scanner" and "qr-scanner" conflict. Both register a camera scanning
surface. Remove one of them.` is the standard.

## Performance

| Budget                       | Limit | Measured    |
| ---------------------------- | ----- | ----------- |
| Validate the maximal fixture | 50 ms | **0.5 ms**  |
| Hash the maximal fixture     | 5 ms  | **0.22 ms** |
| Validate 200 link rules      | 50 ms | **2.8 ms**  |

Two orders of magnitude of headroom. That matters because validation runs on
every keystroke in the studio, and because it will be asked to do considerably
more as the rule set grows.

## Two bugs the double implementation caught

Writing the same engine twice is expensive. It paid for itself twice in one
sprint, and both bugs were of a kind that unit tests alone would not have found.

**1. Unicode normalisation was silently disabled in C#.**
`InvariantGlobalization=true` — the repository default, and a sensible one for
container size — makes `String.Normalize` a no-op for non-ASCII rather than
throwing. The C# canonicaliser therefore was not NFC-normalising at all. A tab
label written with a combining accent would have hashed differently in C# than
in TypeScript: build-cache misses at best, two validators disagreeing about the
same document at worst, and no error message anywhere.

No fixture contained a decomposed character, so the contract test passed. A unit
test caught it. The corpus now carries one, and ADR 0003 records that anything
consuming this library must opt out of invariant globalization.

**2. The backtracking checker rejected safe patterns.**
The first implementation flagged any quantified group whose body contained a
quantifier — which rejects `^[a-z]+(-[a-z]+)*$`, the ordinary separated-list
idiom, and one of the most common patterns anyone writes. It is safe: each
repetition must consume a literal dash first, so repetitions cannot overlap.

Being wrong in that direction is worse than being permissive, because it makes
the product feel broken on correct input. The check now reasons about _why_ a
nested quantifier is dangerous — the body begins with a quantified atom, or the
alternation branches genuinely overlap — and both implementations are tested
against the same 30-case table.

## Decisions taken

- **ADR 0003** — schema v1: strict on save and lenient on read, secrets
  forbidden and actively detected, content-addressed assets, defaults in the
  schema, `x-` extension escape hatch.
- **ADR 0004** — the three-way cache key, which is what makes the unit economics
  in the master spec §16 work.

## Risks retired

- _"The two validators will drift."_ The shared golden corpus is the whole
  defence, and it is now load-bearing: CI regenerates the goldens from
  TypeScript and asserts them from C#, so either side moving fails the build.
- _"Canonicalisation will be subtly non-deterministic."_ Property tests over
  1,000 generated cases per invariant, plus explicit NFC normalisation and
  shortest round-trip number formatting, plus byte-for-byte agreement with a
  second implementation on a different runtime.

## Carried into Sprint 02

Nothing. The schema is the input Sprint 02 consumes, and it is ready.

The one thing to watch: the schema will turn out wrong somewhere. That is what
the migration framework is for, and it is built and tested before it is needed
rather than after two hundred customers have stored configurations.
