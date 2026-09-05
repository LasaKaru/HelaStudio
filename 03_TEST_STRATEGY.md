# Test Strategy

Every sprint file lists test case IDs per task. This document defines what those IDs mean, how tests are written, and what gates them.

---

## 1. Test case ID scheme

```
TC-S07-BLD-014
   │   │   │
   │   │   └── sequence within area
   │   └────── area code
   └────────── sprint that introduced it
```

| Code  | Area                                                   |
| ----- | ------------------------------------------------------ |
| `CFG` | Config schema, validation, migration, canonicalisation |
| `GEN` | Codegen (project generation)                           |
| `BLD` | Build orchestration and runners                        |
| `AND` | Android shell runtime                                  |
| `IOS` | iOS shell runtime                                      |
| `BRG` | JavaScript bridge                                      |
| `PLG` | Plugin system                                          |
| `API` | Control plane API                                      |
| `STU` | App Studio frontend                                    |
| `PUB` | Publishing / store automation                          |
| `SEC` | Security                                               |
| `PRF` | Performance / budget                                   |
| `E2E` | End-to-end user journey                                |

**A test ID is permanent.** If a test is deleted, its ID is retired, never reused. This keeps the traceability matrix meaningful over 27 sprints.

---

## 2. The pyramid, sized for this product

```
        ╱╲          E2E (≈ 20 total)          slow, brittle, priceless
       ╱  ╲         real config → real APK/IPA → real device
      ╱────╲
     ╱      ╲       Integration (≈ 200)        Testcontainers, real Postgres,
    ╱        ╲                                 real Gradle, real xcodebuild
   ╱──────────╲
  ╱            ╲    Contract (≈ 60)            schema, bridge protocol,
 ╱              ╲                              plugin manifest, OpenAPI
╱────────────────╲
      Unit (≈ 1200)                            pure logic, fast, deterministic
```

**Where the value actually is in this product:** contract tests and golden-file codegen tests. A unit test on a validator is cheap insurance; a golden-file test that catches a one-character change in a generated `AndroidManifest.xml` is what stops you shipping 500 broken apps.

---

## 3. Test types and tooling

| Type                   | Layer         | Tooling                                                                                          | Runs in                                                   |
| ---------------------- | ------------- | ------------------------------------------------------------------------------------------------ | --------------------------------------------------------- |
| Unit                   | C#            | xUnit + FluentAssertions + NSubstitute                                                           | PR, < 30 s total                                          |
| Unit                   | TypeScript    | Vitest                                                                                           | PR                                                        |
| Unit                   | Kotlin        | JUnit 5 + Turbine + MockK                                                                        | PR                                                        |
| Unit                   | Swift         | Swift Testing (`@Test`)                                                                          | PR                                                        |
| Property-based         | C#/TS         | FsCheck / fast-check                                                                             | PR — used for canonicalisation, schema migration, hashing |
| Golden file / snapshot | Codegen       | Verify (C#) — approved snapshots committed                                                       | PR                                                        |
| Contract               | Schema        | Ajv + JsonSchema.Net against a shared fixture corpus                                             | PR                                                        |
| Contract               | Bridge        | Shared JSON fixture suite executed against **all three** implementations (TS SDK, Kotlin, Swift) | PR                                                        |
| Integration            | API + DB      | Testcontainers (Postgres, Redis)                                                                 | PR                                                        |
| Integration            | Build         | Real Gradle build in a container; real `xcodebuild` on macOS runner                              | Nightly + on `shells/**` changes                          |
| UI                     | Studio        | Vitest + Testing Library; Playwright for flows                                                   | PR (component), nightly (Playwright)                      |
| UI                     | Android shell | Espresso + Compose UI test                                                                       | Nightly                                                   |
| UI                     | iOS shell     | XCUITest                                                                                         | Nightly                                                   |
| E2E                    | Full pipeline | Playwright driving the studio → real build → install on emulator/simulator → assert              | Nightly + pre-release                                     |
| Performance            | Shell startup | Macrobenchmark (Android), `os_signpost` + XCTest metrics (iOS)                                   | Nightly, tracked over time                                |
| Performance            | API           | k6 smoke on every PR, load nightly                                                               | PR + nightly                                              |
| Security               | Static        | Semgrep, gitleaks, Trivy, `dotnet list package --vulnerable`                                     | PR, blocking                                              |
| Accessibility          | Studio        | axe-core in Playwright                                                                           | Nightly                                                   |
| Accessibility          | Shells        | Accessibility Scanner / Accessibility Inspector, manual checklist                                | Per release                                               |

---

## 4. Golden-file testing for codegen — the core technique

Codegen is the highest-risk component: a bad template silently breaks every app built afterwards.

**Method:**

1. Maintain a **fixture corpus** of configs in `tests/fixtures/configs/`:
   - `minimal.json` — smallest valid config
   - `maximal.json` — every field populated
   - `all-plugins.json` — every plugin enabled
   - `unicode.json` — RTL app name, emoji, CJK, combining characters
   - `edge-*.json` — one file per known edge case (no tabs, 20 tabs, very long bundle id, etc.)
2. For each fixture, generate the project and **commit the full generated output** as an approved snapshot.
3. Any template change produces a diff. **You must read and approve every diff.** This is the point — it forces a human to see exactly what changed in 500 customers' apps.
4. CI fails on any unapproved diff.

**Rules:**

- Snapshots must be **deterministic**: no timestamps, no random ids, no absolute paths, no map iteration order. Enforce with a normaliser and a test that generates twice and asserts byte equality (`TC-S04-GEN-001`).
- The corpus grows with every bug: **every codegen bug fix adds a fixture.** The corpus becomes your regression suite for free.

---

## 5. Bridge contract testing — three implementations, one truth

The bridge exists in TypeScript, Kotlin, and Swift. They must agree exactly.

- Maintain `packages/bridge-protocol/fixtures/*.json`: for each method, `{ input, expectedEnvelope, mockNativeResponse, expectedResult }`.
- Each implementation has a test runner that loads the same fixture files.
- CI fails if any implementation disagrees.
- Adding a bridge method **requires** adding fixtures first. No fixtures, no merge.

This is the cheapest possible defence against the classic hybrid-platform failure: the SDK and the shell drifting apart across versions.

---

## 6. Coverage gates

| Component                  | Line coverage | Branch coverage | Enforced from                 |
| -------------------------- | ------------- | --------------- | ----------------------------- |
| Config schema & validation | 95%           | 90%             | S01                           |
| Codegen                    | 90%           | 85%             | S04                           |
| Control plane API          | 80%           | 70%             | S06                           |
| Build orchestration        | 80%           | 70%             | S07                           |
| Bridge (all 3 impls)       | 90%           | 85%             | S09                           |
| Plugin system              | 85%           | 80%             | S10                           |
| Shell UI code              | 60%           | —               | S12 (UI tests carry the rest) |
| Studio frontend            | 70%           | —               | S12                           |

⚠️ **Coverage is a floor, not a goal.** A PR that raises coverage while adding no assertions is a failed review. The gates exist to catch untested new code, not to be optimised.

**Mutation testing** (Stryker.NET / StrykerJS) on the config validator and codegen only, run weekly. These two components justify the cost; nothing else does.

---

## 7. CI gates

### On every PR (must finish in < 10 minutes)

```
lint  →  unit  →  contract  →  integration (Testcontainers)  →  build studio
                                                             →  size-limit check
                                                             →  gitleaks + semgrep
                                                             →  golden-file diff check
```

Any red = no merge. No exceptions, no "I'll fix it after".

### Nightly (may take 60+ minutes)

```
real Android build from 5 fixture configs  →  install on emulator  →  Espresso smoke
real iOS build from 5 fixture configs      →  install on simulator →  XCUITest smoke
E2E Playwright journeys
performance benchmarks (recorded to a time series)
dependency vulnerability scan
plugin combination matrix (see §8)
```

### Pre-release

```
full nightly suite
+ physical device smoke (one real Android, one real iPhone)
+ manual accessibility checklist
+ store-readiness self-audit on the reference app
```

---

## 8. Plugin combination matrix testing

With N plugins there are 2^N configurations. Test a chosen subset, not the whole space.

| Set             | What                                                           | When                 | Size at 15 plugins |
| --------------- | -------------------------------------------------------------- | -------------------- | ------------------ |
| Singles         | Each plugin alone                                              | Nightly              | 15 builds          |
| **All-pairs**   | Pairwise coverage (covering array)                             | Nightly              | ~25 builds         |
| All-on          | Every plugin enabled                                           | Nightly              | 2 builds           |
| Known-conflicts | Every declared conflict, asserting the _config-time_ rejection | PR (no build needed) | fast               |
| Top-N real      | The 20 most common real customer combinations                  | Nightly, from S17    | 20 builds          |

All-pairs catches the overwhelming majority of real dependency-conflict bugs at a fraction of exhaustive cost. Generate the covering array with a standard tool; do not hand-curate it.

---

## 9. Test data & environments

| Environment  | Purpose          | Data                                           |
| ------------ | ---------------- | ---------------------------------------------- |
| `local`      | Development      | Docker Compose, seeded fixtures                |
| `ci`         | PR gates         | Ephemeral Testcontainers, no shared state      |
| `staging`    | Pre-release, E2E | Anonymised; its own Apple/Google test accounts |
| `production` | Live             | —                                              |

- **Test websites:** host 3 fixture sites on Cloudflare Pages — `simple` (static), `spa` (client-routed React), `auth` (login + cookies + OAuth redirect). These are your test targets for shell behaviour, and they cost nothing.
- **Test store accounts:** one Apple app id and one Play app reserved purely for automated submission tests. Never test against a customer's listing.
- ⚠️ **Never use production signing material in any test.** Generate throwaway self-signed material for test builds.

---

## 10. Manual test checklists

Some things cannot be automated. Maintain these as living checklists in `/docs/qa/`:

- **Physical device smoke** (per release): cold start, tab navigation, back gesture, rotate, background/foreground, airplane mode → offline page → reconnect, deep link from a message, push receipt and tap-through, file upload from camera and gallery, external link handling, biometric prompt.
- **Store submission dry run** (per release): screenshots present at all required sizes, privacy manifest present, data safety form complete, age rating set, export compliance answered.
- **Accessibility** (per release): full VoiceOver traversal, full TalkBack traversal, 200% text size, high contrast, keyboard/switch navigation.

---

## 11. Defect management

| Severity | Definition                                          | Response                               |
| -------- | --------------------------------------------------- | -------------------------------------- |
| **S1**   | Builds broken for all users, or a security incident | Stop the sprint. Fix now. Post-mortem. |
| **S2**   | A feature is broken for many users; no workaround   | Fix in current sprint                  |
| **S3**   | Broken with a workaround, or affects few users      | Next sprint                            |
| **S4**   | Cosmetic / minor                                    | Backlog                                |

**Every S1 and S2 defect gets a regression test with a new TC ID before the fix is merged.** No exceptions. This is how the suite grows to match reality instead of matching your imagination.

---

## 12. Performance budgets (asserted in CI, not aspirational)

| Metric                                 | Budget   | Test                             |
| -------------------------------------- | -------- | -------------------------------- |
| Android shell cold start (first frame) | < 300 ms | Macrobenchmark, `TC-S02-PRF-001` |
| Android shell interactive              | < 500 ms | Macrobenchmark                   |
| iOS shell cold start (first frame)     | < 300 ms | XCTest metric, `TC-S03-PRF-001`  |
| Base APK size (arm64 split)            | < 12 MB  | CI size check                    |
| Base IPA size                          | < 25 MB  | CI size check                    |
| Studio initial JS (gzipped)            | < 200 KB | `size-limit`                     |
| Studio LCP (4× throttled)              | < 2.0 s  | Lighthouse CI                    |
| API p95 (config read)                  | < 100 ms | k6                               |
| API p95 (config save + validate)       | < 400 ms | k6                               |
| Config validation (maximal fixture)    | < 50 ms  | Benchmark                        |
| Codegen (maximal fixture)              | < 3 s    | Benchmark                        |
| Android cloud build p95                | < 6 min  | Pipeline metric                  |
| iOS cloud build p95                    | < 15 min | Pipeline metric                  |
| Cache-hit build (asset-only)           | < 90 s   | Pipeline metric                  |

A budget regression fails CI. Budgets are raised only by an explicit, justified decision recorded in the PR — never by quietly editing the number.
