# Journal

Three lines each working day: done, today, blocked. Blocked for more than two
days means a scope cut, not a longer entry (`00_MASTER_SPRINT_PLAN.md` §5).

---

## 2026-08-31 — S00 and S01

**Done.** Monorepo, CI, standards enforcement, and the test harness stood up.
`appconfig.json` v1 authored, with validation, canonicalisation, the three-way
hash split, and the migration framework built twice — TypeScript and C# — and
locked together by a golden-file contract test. 29 fixtures, 326 tests green.

**Today.** Sprint 02: hand-write the Android shell.

**Blocked.** Nothing in code. Three Sprint 00 tasks need real accounts and cannot
be done from here: the Oracle host (T-00.5), the Cloudflare bucket and Pages
project (T-00.6), and the credit and student-pack applications (T-00.1). Each is
written up step by step in `docs/ops/provisioning.md`. ⚠️ The Codemagic student
application should go in first — it takes days to process and it is what removes
the 500 macOS-minute ceiling that otherwise constrains all of Phase 0 and 1.

**Worth recording.** Two findings from building the same engine twice:

1. `InvariantGlobalization=true` — the repository default, and a sensible one for
   container size — silently makes `String.Normalize` a no-op for non-ASCII. The
   C# canonicaliser was therefore not NFC-normalising at all, so a decomposed
   accent would have hashed differently in C# than in TypeScript: cache misses,
   and two validators disagreeing. No fixture had a decomposed character, so the
   contract test did not catch it; a unit test did. The corpus now carries one.

2. The first backtracking checker rejected `^[a-z]+(-[a-z]+)*$` — the ordinary
   separated-list idiom, and safe, because each repetition must consume a literal
   dash first. Flagging a pattern that common would have been a daily
   irritation. The check is now structural about _why_ a nested quantifier is
   dangerous rather than merely spotting the shape.
