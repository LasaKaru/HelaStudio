# Sprint 00 review — Foundations and development environment

**Goal:** stand up the development substrate so that from Sprint 01 onward every
commit is linted, tested, and gated automatically.

## Exit criteria

| Criterion                                                              | Status                                                      |
| ---------------------------------------------------------------------- | ----------------------------------------------------------- |
| Monorepo exists, builds green on CI, all toolchains configured         | ✅                                                          |
| A trivial commit runs lint → unit → secrets → build in under 5 minutes | ✅ path-filtered, cancel-in-progress, NuGet and pnpm caches |
| `TreatWarningsAsErrors` / `strict` everywhere; zero warnings           | ✅                                                          |
| `COSTS.md`, `JOURNAL.md`, `CHANGELOG.md`, ADR folder, PR template      | ✅                                                          |
| Test harness skeleton proven                                           | ✅ 193 TypeScript and 133 C# tests                          |
| Three fixture test websites                                            | ✅ built and verified locally; deployment pending           |
| Oracle Always Free instance provisioned                                | ⏳ needs an account                                         |
| Cloudflare R2 bucket and Pages project                                 | ⏳ needs an account                                         |
| Billing alerts at $10 on every cloud account                           | ⏳ needs an account                                         |
| Codemagic and GitHub Student Pack applications                         | ⏳ needs an account                                         |

Everything that is code is done. The four outstanding items all require real
cloud accounts and a payment card, which a development container does not have.
Each is written up step by step, with its pitfalls, in
`docs/ops/provisioning.md`, and each keeps its original test case ID.

⚠️ Do the Codemagic student application first. It takes days to process, and it
is what removes the 500 macOS-minute ceiling that otherwise constrains all of
Phase 0 and Phase 1.

## What shipped

- pnpm workspaces with Turborepo, and a .NET 10 solution, in one tree.
- Standards made mechanical rather than aspirational: Roslyn at
  `latest-all` with warnings as errors, ESLint `strict-type-checked` with
  `noUncheckedIndexedAccess` and `exactOptionalPropertyTypes`, Prettier,
  `.editorconfig` covering all six languages, lefthook, and Conventional Commits.
- CI with path filtering, run cancellation, dependency caching, and a single
  aggregated `gate` check so branch protection never needs reconfiguring.
- Secret scanning tuned for this product specifically: Apple `.p8` and `.p12`
  shapes, Google service-account JSON, Android keystore passwords.
- Three fixture websites — static, client-routed, and cookie-authenticated with
  a working mock OAuth redirect chain — behind a dependency-free server.
- Studio scaffold with the 200 kB gzipped budget enforced from day one. It is at
  109 kB, which means the budget will be a real constraint later, not a formality.

## Decisions taken

- **ADR 0002** — the shells become separate public repositories. Decided now,
  while both directories are empty, because splitting a repository with history
  is painful and the CI-minutes rationale only pays out if the split precedes
  the first macOS build in Sprint 03.

## What would have gone wrong without this sprint

The scaffold is not the point; the gates are. Coverage thresholds, warnings as
errors, and the golden-file checks all landed before there was any code to
grandfather in. Retrofitting them onto forty files costs about three times as
much, and in practice usually means they get softened instead.

## Carried into Sprint 02

Nothing from the code. The four provisioning tasks move with their test IDs
intact — they are not dropped, they are blocked on an account.
