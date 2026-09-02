# Shellwright

A cloud platform that takes a URL and a configuration, and produces signed,
store-ready iOS and Android apps: native chrome, a live web view, and a
JavaScript bridge that lets the website drive real device capability.

The user never installs Xcode, never touches a certificate, and never learns
Swift or Kotlin.

**Status:** Phase 1, Sprint 06. The configuration schema, the validation engine,
both native shells, code generation for both platforms, and the multi-tenant
control plane are built. The remaining Sprint 03 criteria are the M1 kill gate —
one app on TestFlight and on Play internal testing — and are blocked on
developer-account enrolment and physical devices rather than on code. See
[`ACTION_REQUIRED.md`](ACTION_REQUIRED.md).

An `appconfig.json` generates complete Android **and** iOS projects, with icons
rendered from one uploaded source, and the API stores those configurations as
immutable content-addressed versions behind row-level security. The Android
project builds into a real 848 kB APK.

⚠️ Two things are unproven rather than done. The iOS generator has never met a
real toolchain — nothing here runs `xcodebuild` — so it stays unproven until the
Codemagic `ios-verify` workflow runs. And the API's OAuth flow has never
completed a real authorisation code exchange, because that needs live
credentials at both providers.

## Run it locally

Needs Node 22, pnpm 10, and the .NET 10 SDK.

```bash
pnpm install
pnpm build          # config schema, then the studio
pnpm test           # 241 TypeScript tests
dotnet test         # 533 C# tests, including the cross-language contract
pnpm verify         # everything CI runs, the way CI runs it
pnpm --filter @shellwright/studio dev   # the studio on http://localhost:5173
```

⚠️ `dotnet test` needs a real PostgreSQL for the control plane's suite, and the
fixture starts one itself if the connection strings are not already in the
environment. There is no in-memory substitute: what those tests assert — that a
policy denies a row, that a role cannot `UPDATE` a table — are properties of
PostgreSQL, and a fake would agree with whatever we claimed. Bring one up by
hand with `bash scripts/dev-postgres.sh` if you would rather.

## What is here

| Path                            | What it is                                                                                                                            |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `packages/config-schema`        | `appconfig.json` v1 — schema, validation, canonicalisation, hashing, migration. TypeScript and C#, held identical by a contract test. |
| `apps/studio`                   | The browser app. Today it validates a pasted configuration; Sprint 11 turns it into the visual editors.                               |
| `apps/api`                      | The control plane: identity, organisations, apps, and immutable config versions, isolated by row-level security. Sprint 06.           |
| `packages/api-client`           | TypeScript types generated from the API's own OpenAPI document. Nothing here is written by hand.                                      |
| `services/orchestrator`         | Build orchestration. Sprint 07.                                                                                                       |
| `shells/android`, `shells/ios`  | The native runtimes. Separate public repositories — see ADR 0002. Sprints 02 and 03.                                                  |
| `tests/fixtures/configs`        | 32 configurations: valid, edge, and one per diagnostic code.                                                                          |
| `tests/load`                    | k6 scripts behind the performance budgets. See `docs/perf/baseline-s06.md`.                                                           |
| `tests/fixtures/expected`       | The goldens both validators must reproduce byte for byte.                                                                             |
| `tests/fixtures/sites`          | Three controlled websites to point the shells at.                                                                                     |
| `docs/adr`                      | Why the one-way doors were decided the way they were.                                                                                 |
| `docs/reference/diagnostics.md` | Every diagnostic code, what it means, how to fix it.                                                                                  |

## The idea in one paragraph

Median.co has led this category since 2014, and roughly 70% of the price gap
between its free and top tiers is charged for things that cost it nothing per
user: removing a watermark, allowing more plugins, adding a team seat. The
strategy here inverts that. Every software capability is free — every plugin,
watermark-free builds, unlimited seats, full source export. Revenue comes from
what genuinely costs something: iOS build and simulator minutes beyond a
generous allowance, first-party push and analytics at volume, managed
publishing, and enterprise controls. See `SHELLWRIGHT_MASTER_SPEC.md`.

## Working on it

Read `01_ENGINEERING_STANDARDS.md` before writing code, and its §9 review
checklist before every merge. `03_TEST_STRATEGY.md` defines what the test IDs
mean and what gates CI. Each sprint's exit criteria live in its own file under
`sprints/`; they are not softened mid-sprint.

Contributing conventions are in `CONTRIBUTING.md`.
