# Changelog

All notable changes to this project are recorded here, in the
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. This project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Sprint 00 — Foundations

- Monorepo scaffold: pnpm workspaces, Turborepo, and a .NET 10 solution.
- Standards enforcement: `TreatWarningsAsErrors` and `AnalysisLevel=latest-all`
  for C#; ESLint `strict-type-checked` with `noUncheckedIndexedAccess` and
  `exactOptionalPropertyTypes` for TypeScript; Prettier, `.editorconfig`,
  lefthook pre-commit hooks, and Conventional Commits.
- CI pipeline with path filtering, run cancellation, dependency caching, a
  single aggregated `gate` status check, and a nightly stub.
- Secret scanning tuned for this product: Apple `.p8`/`.p12` shapes, Google
  service-account JSON, and Android keystore passwords.
- Three fixture websites — static, client-routed, and cookie-authenticated with
  a mock OAuth redirect chain — served by a dependency-free local server.
- App Studio scaffold: React 18 and Vite, with a 200 kB gzipped bundle budget
  enforced by `size-limit` (currently 109 kB).
- ADR 0001 (record decisions) and ADR 0002 (monorepo with public shells).

#### Sprint 01 — Configuration schema and validation

- `appconfig.json` v1 as JSON Schema 2020-12, with a user-facing `title` and
  `description` on every property so the studio renders help text for free.
- Validation engine in **both** TypeScript and C#, producing byte-identical
  diagnostics, canonical forms, and cache keys — asserted against a shared
  golden corpus by a cross-language contract test.
- 19 semantic rules covering store-rejection causes that JSON Schema cannot see:
  origin coverage, catastrophic regex backtracking, unjustified permissions,
  plugin conflicts and platform floors, icon alpha channels, and credentials
  pasted into configuration.
- Canonical JSON: key-sorted, NFC-normalised, shortest round-trip numbers,
  nulls omitted. Property-tested for order-independence over 1,000 generated
  cases per invariant.
- Three-way BLAKE3 cache key (`codeKey`, `assetKey`, `contentKey`) so an
  asset-only or content-only change skips the full recompile path.
- Migration framework with a proven, reversible v0-to-v1 path and golden files.
- Fixture corpus of 29 configurations, including one per diagnostic code.
- Generated TypeScript types, with a CI check that regeneration produces no diff.
- ADR 0003 (schema v1) and ADR 0004 (three-way cache key).
- `docs/reference/diagnostics.md`, generated from the code table.

### Performance

Measured against the budgets in `03_TEST_STRATEGY.md` §12, asserted in CI:

| Budget                          | Limit  | Measured |
| ------------------------------- | ------ | -------- |
| Validate the maximal fixture    | 50 ms  | 0.5 ms   |
| Hash the maximal fixture        | 5 ms   | 0.22 ms  |
| Validate 200 link rules         | 50 ms  | 2.8 ms   |
| Studio initial bundle (gzipped) | 200 kB | 109 kB   |
