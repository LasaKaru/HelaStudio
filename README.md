# Shellwright

A cloud platform that takes a URL and a configuration, and produces signed,
store-ready iOS and Android apps: native chrome, a live web view, and a
JavaScript bridge that lets the website drive real device capability.

The user never installs Xcode, never touches a certificate, and never learns
Swift or Kotlin.

**Status:** Phase 0, Sprint 03. The configuration schema, the validation engine,
and both native shells are built. The remaining Sprint 03 criteria are the M1
kill gate — one app on TestFlight and on Play internal testing — and are blocked
on developer-account enrolment and physical devices rather than on code. See
[`ACTION_REQUIRED.md`](ACTION_REQUIRED.md).

## Run it locally

Needs Node 22, pnpm 10, and the .NET 10 SDK.

```bash
pnpm install
pnpm build          # config schema, then the studio
pnpm test           # 193 TypeScript tests
dotnet test         # 133 C# tests, including the cross-language contract
pnpm --filter @shellwright/studio dev   # the studio on http://localhost:5173
```

## What is here

| Path                                | What it is                                                                                                                            |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| `packages/config-schema`            | `appconfig.json` v1 — schema, validation, canonicalisation, hashing, migration. TypeScript and C#, held identical by a contract test. |
| `apps/studio`                       | The browser app. Today it validates a pasted configuration; Sprint 11 turns it into the visual editors.                               |
| `apps/api`, `services/orchestrator` | Control plane and build orchestration. Sprints 06 and 07.                                                                             |
| `shells/android`, `shells/ios`      | The native runtimes. Separate public repositories — see ADR 0002. Sprints 02 and 03.                                                  |
| `tests/fixtures/configs`            | 29 configurations: valid, edge, and one per diagnostic code.                                                                          |
| `tests/fixtures/expected`           | The goldens both validators must reproduce byte for byte.                                                                             |
| `tests/fixtures/sites`              | Three controlled websites to point the shells at.                                                                                     |
| `docs/adr`                          | Why the one-way doors were decided the way they were.                                                                                 |
| `docs/reference/diagnostics.md`     | Every diagnostic code, what it means, how to fix it.                                                                                  |

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
