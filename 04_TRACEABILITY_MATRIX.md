# Traceability Matrix

Maps every requirement from `SHELLWRIGHT_MASTER_SPEC.md` §12 to the sprint that delivers it and the test IDs that verify it. Update at every sprint review.

**Status key:** ☐ not started · ◐ in progress · ☑ done · ⊘ deferred (with a reason)

---

## 1. App Studio (`ST-*`)

| Req   | Description                                          | Sprint        | Test IDs                                       | Status                          |
| ----- | ---------------------------------------------------- | ------------- | ---------------------------------------------- | ------------------------------- |
| ST-01 | URL ingestion + site analysis                        | S11           | `TC-S11-STU-019`–`024`                         | ☐                               |
| ST-02 | Icon generator (all densities, adaptive, monochrome) | S04, S11      | `TC-S04-GEN-019`–`028`, `TC-S11-STU-043`–`048` | ☐                               |
| ST-03 | Splash screen designer                               | S04, S05, S11 | `TC-S11-STU-049`–`051`                         | ☐                               |
| ST-04 | Theme editor with live preview                       | S11           | `TC-S11-STU-052`–`056`                         | ☐                               |
| ST-05 | Navigation designer                                  | S12           | `TC-S12-STU-001`–`012`                         | ☐                               |
| ST-06 | Link-rule editor with tester                         | S12           | `TC-S12-STU-013`–`020`                         | ☐                               |
| ST-07 | Plugin catalogue with config forms                   | S12           | `TC-S12-STU-021`–`032`                         | ☐                               |
| ST-08 | Raw JSON editor with schema validation               | S12           | `TC-S12-STU-047`–`056`                         | ☐                               |
| ST-09 | Live device preview                                  | S13           | `TC-S13-PV-*`                                  | ☐                               |
| ST-10 | Build history with logs and config diff              | S12           | `TC-S12-STU-033`–`046`                         | ☐                               |
| ST-11 | Environments (dev/staging/prod)                      | S19           | `TC-S19-*`                                     | ☐                               |
| ST-12 | Config diff and rollback                             | S11           | `TC-S11-STU-041`                               | ☐                               |
| ST-13 | Team workspace, roles, audit log                     | S06           | `TC-S06-API-017`–`024`                         | ☐                               |
| ST-14 | Agency mode with client sub-workspaces               | S25           | `TC-S25-*`                                     | ☐                               |
| ST-15 | Template gallery                                     | S19           | —                                              | ⊘ P1, move if capacity is tight |
| ST-16 | AI config assistant                                  | —             | —                                              | ⊘ P2, post-GA                   |

## 2. Native shell runtime (`RT-*`)

| Req   | Description                                             | Sprint   | Test IDs                                       | Status                        |
| ----- | ------------------------------------------------------- | -------- | ---------------------------------------------- | ----------------------------- |
| RT-01 | WebView host with config-driven chrome                  | S02, S03 | `TC-S02-AND-004`–`012`, `TC-S03-IOS-002`–`012` | ☐                             |
| RT-02 | Tab bar, nav bar, drawer, natively rendered             | S02, S03 | `TC-S02-AND-013`–`020`, `TC-S03-IOS-013`–`020` | ☐                             |
| RT-03 | Multi-window / modal WebView stack                      | S02, S03 | `TC-S02-AND-018`                               | ☐                             |
| RT-04 | Pull-to-refresh, swipe-back, edge gestures              | S02, S03 | `TC-S02-AND-017`, `TC-S03-IOS-018`             | ☐                             |
| RT-05 | Offline page + connectivity events                      | S02, S03 | `TC-S02-AND-029`–`034`                         | ☐                             |
| RT-06 | ⭐ Instant shell paint < 100 ms                         | S02, S03 | `TC-S02-PRF-001`, `TC-S03-PRF-002`             | ☐                             |
| RT-07 | Cookie persistence, headers, UA, CSS/JS injection       | S02, S03 | `TC-S02-AND-009`, `TC-S03-IOS-005`             | ☐                             |
| RT-08 | ⭐ `ASWebAuthenticationSession` / Custom Tabs for OAuth | S03      | `TC-S03-IOS-006`                               | ☐                             |
| RT-09 | Universal Links / App Links + custom schemes            | S04, S05 | `TC-S04-GEN-013`, `TC-S05-GEN-013`             | ☐                             |
| RT-10 | Native encrypted datastore                              | S10, S18 | `TC-S10-SEC-001`                               | ☐                             |
| RT-11 | Download manager with resume                            | S23      | `TC-S23-*`                                     | ☐                             |
| RT-12 | Secure modal (screenshot suppression)                   | S18      | `TC-S18-PLG-*`                                 | ☐                             |
| RT-13 | Share-into-app                                          | S18      | `TC-S18-PLG-*`                                 | ☐                             |
| RT-14 | ⭐ Declarative native surfaces                          | S24      | `TC-S24-*`                                     | ☐                             |
| RT-15 | ⭐ Declarative widgets / Live Activities                | —        | —                                              | ⊘ post-GA (master spec §8.3B) |
| RT-16 | ⭐ Declarative App Intents / Shortcuts                  | —        | —                                              | ⊘ post-GA (master spec §8.3C) |
| RT-17 | ⭐ OTA bundle loader with rollback                      | S22      | `TC-S22-SV-*`                                  | ☐                             |
| RT-18 | ⭐ Accessibility bridge across native↔web              | S24, S26 | `TC-S24-*`, `TC-S26-A11Y-*`                    | ☐                             |
| RT-19 | Minimum WebView version check                           | S02      | `TC-S02-AND-*`                                 | ☐                             |

## 3. JavaScript Bridge (`BR-*`)

| Req   | Description                       | Sprint | Test IDs                | Status                     |
| ----- | --------------------------------- | ------ | ----------------------- | -------------------------- |
| BR-01 | Versioned promise-based API       | S09    | `TC-S09-BRG-001`–`004`  | ☐                          |
| BR-02 | npm package with TypeScript types | S09    | `TC-S09-BRG-025`–`036`  | ☐                          |
| BR-03 | ⭐ Capability negotiation         | S09    | `TC-S09-BRG-041`        | ☐                          |
| BR-04 | ⭐ Browser no-op shim, SSR-safe   | S09    | `TC-S09-BRG-027`, `030` | ☐                          |
| BR-05 | Event bus                         | S09    | `TC-S09-BRG-*`          | ☐                          |
| BR-06 | SPA navigation integration        | S09    | `TC-S09-BRG-*`          | ☐                          |
| BR-07 | Structured typed errors           | S09    | `TC-S09-BRG-009`        | ☐                          |
| BR-08 | ⭐ Bridge inspector               | S09    | `TC-S09-BRG-043`, `044` | ☐                          |
| BR-09 | iframe-safe callbacks             | —      | —                       | ⊘ P2                       |
| BR-10 | React/Vue/Svelte hook packages    | S09    | —                       | ⊘ P1, React only initially |

## 4. Plugin system (`PL-*`)

| Req   | Description                          | Sprint   | Test IDs                           | Status    |
| ----- | ------------------------------------ | -------- | ---------------------------------- | --------- |
| PL-01 | Manifest-driven plugin format        | S10      | `TC-S10-PLG-001`–`008`             | ☐         |
| PL-02 | Build-time injection (seven outputs) | S10      | `TC-S10-PLG-009`–`026`             | ☐         |
| PL-03 | Conflict detection at config time    | S10      | `TC-S10-PLG-027`–`042`             | ☐         |
| PL-04 | Permission-string management         | S10      | `TC-S10-PLG-014`, `018`            | ☐         |
| PL-05 | Privacy manifest fragments           | S05, S10 | `TC-S05-GEN-015`, `TC-S10-PLG-018` | ☐         |
| PL-06 | Data Safety fragments                | S10, S16 | `TC-S16-PUB-*`                     | ☐         |
| PL-07 | Private/custom plugins per tenant    | S25      | `TC-S25-*`                         | ☐         |
| PL-08 | Public plugin SDK                    | —        | —                                  | ⊘ post-GA |

## 5. Build service (`BD-*`)

| Req   | Description                              | Sprint        | Test IDs                           | Status    |
| ----- | ---------------------------------------- | ------------- | ---------------------------------- | --------- |
| BD-01 | Deterministic codegen both platforms     | S04, S05      | `TC-S04-GEN-029`, `TC-S05-GEN-004` | ☐         |
| BD-02 | Android APK/AAB                          | S07           | `TC-S07-BLD-*`                     | ☐         |
| BD-03 | iOS IPA                                  | S08           | `TC-S08-BLD-017`–`028`             | ☐         |
| BD-04 | Signing: managed / BYO / delegated       | S14           | `TC-S14-SEC-*`                     | ☐         |
| BD-05 | App Store Connect API signing automation | S08, S14      | `TC-S08-BLD-019`                   | ☐         |
| BD-06 | Android keystore generation and export   | S14           | `TC-S14-SEC-*`                     | ☐         |
| BD-07 | Live build logs                          | S07           | `TC-S07-BLD-029`–`034`             | ☐         |
| BD-08 | ⭐ Reproducible builds                   | S04, S05, S08 | `TC-S04-GEN-029`                   | ☐         |
| BD-09 | Toolchain version pinning per app        | S08           | `TC-S08-BLD-047`, `048`            | ☐         |
| BD-10 | ⭐ Full source export                    | S19           | `TC-S19-*`                         | ☐         |
| BD-11 | ⭐ CLI                                   | S19           | `TC-S19-*`                         | ☐         |
| BD-12 | Build API + webhooks                     | S19           | `TC-S19-*`                         | ☐         |
| BD-13 | ⭐ Bulk rebuild                          | S25           | `TC-S25-*`                         | ☐         |
| BD-14 | Self-hosted runner agent                 | —             | —                                  | ⊘ post-GA |
| BD-15 | PWA / TWA export                         | S19           | `TC-S19-*`                         | ☐         |
| BD-16 | Desktop target (Tauri)                   | —             | —                                  | ⊘ post-GA |

## 6. Preview & testing (`PV-*`)

| Req   | Description                       | Sprint   | Test IDs                           | Status    |
| ----- | --------------------------------- | -------- | ---------------------------------- | --------- |
| PV-01 | Streamed Android emulator         | S13      | `TC-S13-PV-*`                      | ☐         |
| PV-02 | Streamed iOS simulator            | S13      | `TC-S13-PV-*`                      | ☐         |
| PV-03 | Remote web inspector              | S13      | `TC-S13-PV-*`                      | ☐         |
| PV-04 | QR-to-device install              | S12      | `TC-S12-STU-044`                   | ☐         |
| PV-05 | ⭐ Automated smoke test per build | S04, S05 | `TC-S04-BLD-002`, `TC-S05-BLD-002` | ☐         |
| PV-06 | ⭐ Screenshot generator           | S16      | `TC-S16-PUB-*`                     | ☐         |
| PV-07 | Visual regression diff            | —        | —                                  | ⊘ post-GA |
| PV-08 | Real-device cloud testing         | —        | —                                  | ⊘ post-GA |

## 7. Publishing & compliance (`PB-*`)

| Req   | Description                                  | Sprint   | Test IDs                         | Status                           |
| ----- | -------------------------------------------- | -------- | -------------------------------- | -------------------------------- |
| PB-01 | Guided publishing wizard                     | S15      | `TC-S15-PUB-*`                   | ☐                                |
| PB-02 | ⭐ Store Readiness Score                     | S16      | `TC-S16-PUB-*`                   | ☐                                |
| PB-03 | TestFlight upload automation                 | S15      | `TC-S15-PUB-*`                   | ☐                                |
| PB-04 | Play track upload automation                 | S15      | `TC-S15-PUB-*`                   | ☐                                |
| PB-05 | Privacy manifest generator                   | S05, S16 | `TC-S05-GEN-015`, `TC-S16-PUB-*` | ☐                                |
| PB-06 | Data Safety form generator                   | S16      | `TC-S16-PUB-*`                   | ☐                                |
| PB-07 | Age rating assistant                         | S16      | `TC-S16-PUB-*`                   | ☐                                |
| PB-08 | DSA trader-status guidance                   | S16      | —                                | ☐                                |
| PB-09 | ⭐ Android developer verification onboarding | S03, S16 | `TC-S03-PUB-006`                 | ☐                                |
| PB-10 | ⭐ Rejection knowledge base                  | S03, S16 | —                                | ☐                                |
| PB-11 | Store listing management                     | —        | —                                | ⊘ post-GA                        |
| PB-12 | Managed publishing service                   | —        | —                                | ⊘ post-GA (revenue, not product) |
| PB-13 | Alternative stores                           | —        | —                                | ⊘ post-GA                        |

## 8. First-party services (`SV-*`)

| Req   | Description                         | Sprint | Test IDs      | Status    |
| ----- | ----------------------------------- | ------ | ------------- | --------- |
| SV-01 | ⭐ Push service                     | S20    | `TC-S20-SV-*` | ☐         |
| SV-02 | ⭐ Analytics                        | S21    | `TC-S21-SV-*` | ☐         |
| SV-03 | Crash reporting with symbolication  | S21    | `TC-S21-SV-*` | ☐         |
| SV-04 | ⭐ OTA bundle hosting               | S22    | `TC-S22-SV-*` | ☐         |
| SV-05 | Remote config / feature flags       | —      | —             | ⊘ post-GA |
| SV-06 | In-app messaging                    | —      | —             | ⊘ post-GA |
| SV-07 | Attribution / deferred deep linking | —      | —             | ⊘ post-GA |

## 9. Platform / account (`AC-*`)

| Req   | Description                          | Sprint   | Test IDs                         | Status    |
| ----- | ------------------------------------ | -------- | -------------------------------- | --------- |
| AC-01 | Auth, orgs, workspaces, roles        | S06      | `TC-S06-API-007`–`024`           | ☐         |
| AC-02 | Billing with metered usage           | S17      | `TC-S17-API-*`                   | ☐         |
| AC-03 | Usage dashboard                      | S17      | `TC-S17-API-*`                   | ☐         |
| AC-04 | Quota enforcement with soft warnings | S07, S17 | `TC-S07-BLD-039`, `TC-S17-API-*` | ☐         |
| AC-05 | Audit log                            | S06      | `TC-S06-API-*`                   | ☐         |
| AC-06 | SSO / SAML / SCIM                    | —        | —                                | ⊘ post-GA |
| AC-07 | Status page                          | S19      | —                                | ☐         |
| AC-08 | Docs site                            | S12      | `TC-S12-DOC-001`, `002`          | ☐         |

⭐ = differentiator against Median (master spec §10.1)

---

## 10. Differentiation-pillar coverage

Verify at every milestone that each pillar has shipped capability, not just planned capability.

| Pillar                         | Delivered by                                | Gate                                                   |
| ------------------------------ | ------------------------------------------- | ------------------------------------------------------ |
| **1. Zero feature gating**     | S17 (plan gating on compute only)           | M4 — no feature is behind a paywall                    |
| **2. You own your app**        | S19 (source export, CLI, config-as-code)    | M4 — nightly clean-machine export build passes         |
| **3. Native where it counts**  | S24 (surfaces), S02/S03 (native chrome)     | M5 — readiness score reflects native surfaces          |
| **4. Batteries included**      | S20, S21, S22 (push, analytics, OTA)        | M5 — no third-party account required for core function |
| **5. Compliance as a product** | S16 (readiness, generators, knowledge base) | M4 — a bare wrapper cannot be submitted unwarned       |

---

## 11. Hard-constraint coverage

Every ⚠️ constraint from master spec Part III must be visibly handled somewhere.

| Constraint                          | Master spec § | Handled in         | How                                                                             |
| ----------------------------------- | ------------- | ------------------ | ------------------------------------------------------------------------------- |
| Guideline 4.2 minimum functionality | §7.1          | S02, S03, S16, S24 | Native chrome by default, readiness score, native surfaces                      |
| Guideline 4.2.6 template services   | §7.2          | S14, S15           | Delegation-only publishing; customer's own account always                       |
| Android developer verification      | §7.3          | S03, S16           | Registration in Phase 0; onboarding product in S16                              |
| IAP mandate for digital goods       | §7.4          | S16, S18           | Commerce classifier in readiness; free IAP plugin                               |
| Annual OS treadmill                 | §7.5          | All                | 25% permanent capacity reserve; toolchain matrix nightly                        |
| macOS build fleet                   | §18.1         | S08, S25           | Provider abstraction; hosted → owned migration path                             |
| Signing custody                     | §18.2         | S14                | Delegation default; Vault; ephemeral injection; audit                           |
| Plugin combinatorics                | §18.4         | S10, S18           | Manifest-driven injection; config-time conflicts; all-pairs matrix              |
| WebView fidelity / cookies / auth   | §18.5         | S02, S03           | `ASWebAuthenticationSession`, cookie persistence, diagnostics tool              |
| Preview cost and latency            | §18.6         | S13                | Buy Appetize; meter; label honestly                                             |
| True offline limits                 | §18.8         | S23                | Layers 1–2 only; published capability matrix                                    |
| Accessibility across the boundary   | §18.9         | S24, S26           | axe gates, manual audit, published statement                                    |
| Support load                        | §18.10        | S12, S16           | Diagnostics tool, generated docs, readiness checks, community-only free support |

---

## 12. Review procedure

At each sprint review:

1. Update the Status column for every row touched.
2. Add any new test IDs created.
3. For anything moved to ⊘, record the reason and the sprint it moved to — ⚠️ never silently drop a requirement.
4. Confirm the hard-constraint table (§11) still has an owner for every row.
5. If a differentiation pillar (§10) has slipped past its gate milestone, escalate it in the review rather than absorbing it.

---

## Sprint 00 and 01 — as built

Recorded 2026-08-31. Test counts are from the suites that gate CI.

### Sprint 00

| Task   | Deliverable                      | Verified by                                          | Status                                                   |
| ------ | -------------------------------- | ---------------------------------------------------- | -------------------------------------------------------- |
| T-00.1 | Credits, student packs, accounts | `TC-S00-OPS-001`                                     | ⏳ needs accounts — `docs/ops/provisioning.md`           |
| T-00.2 | Monorepo scaffold and tooling    | `TC-S00-CI-001`, `TC-S00-CI-002`                     | ✅                                                       |
| T-00.3 | CI pipeline                      | `TC-S00-CI-003`, `TC-S00-CI-004`, `TC-S00-SEC-001`   | ✅ `.github/workflows/ci.yml`                            |
| T-00.4 | Standards enforcement            | `TC-S00-CI-005`, `TC-S00-CI-006`                     | ✅                                                       |
| T-00.5 | Oracle Always Free host          | `TC-S00-OPS-002`, `TC-S00-OPS-003`, `TC-S00-SEC-002` | ⏳ needs an account                                      |
| T-00.6 | Cloudflare R2, Pages, DNS        | `TC-S00-OPS-004`, `TC-S00-SEC-003`                   | ⏳ needs an account                                      |
| T-00.7 | Test harness and fixture corpus  | `TC-S00-CI-007`, `TC-S00-CI-008`                     | ✅                                                       |
| T-00.8 | Fixture test websites            | `TC-S00-OPS-005`                                     | ✅ built and verified locally; deployment pending T-00.6 |
| T-00.9 | Documentation scaffolding        | —                                                    | ✅                                                       |

### Sprint 01

| Task   | Deliverable                           | Verified by                                          | Status                                                           |
| ------ | ------------------------------------- | ---------------------------------------------------- | ---------------------------------------------------------------- |
| T-01.1 | ADR and schema design                 | —                                                    | ✅ `docs/adr/0003`, `docs/adr/0004`                              |
| T-01.2 | JSON Schema v1                        | `TC-S01-CFG-001`…`010`                               | ✅ 30 definitions                                                |
| T-01.3 | Type generation, C# and TypeScript    | `TC-S01-CFG-011`, `TC-S01-CFG-012`                   | ✅ generated types, CI drift check, cross-language contract test |
| T-01.4 | Validation engine with semantic rules | `TC-S01-CFG-013`…`034`                               | ✅ 19 rules, each with a passing and a failing test              |
| T-01.5 | Canonical JSON and hash split         | `TC-S01-CFG-035`…`042`, `TC-S01-PRF-002`             | ✅ property-tested, 0.22 ms against a 5 ms budget                |
| T-01.6 | Migration framework                   | `TC-S01-CFG-043`…`056`                               | ✅ reversible v0→v1, golden-file verified                        |
| T-01.7 | Fixture corpus                        | `TC-S01-CFG-049`                                     | ✅ 29 fixtures                                                   |
| —      | Performance budgets                   | `TC-S01-PRF-001`, `TC-S01-PRF-003`, `TC-S01-PRF-004` | ✅ 0.5 ms against a 50 ms budget                                 |

### Coverage against the gates in `03_TEST_STRATEGY.md` §6

| Component                    | Required                       | Actual            |
| ---------------------------- | ------------------------------ | ----------------- |
| Config schema and validation | 95% line / 90% branch          | **99.3% / 92.9%** |
| Studio frontend              | 60% line (rises to 70% at S12) | 69.2%             |

### Suite totals

| Suite                                | Tests   |
| ------------------------------------ | ------- |
| TypeScript, `packages/config-schema` | 193     |
| TypeScript, `apps/studio`            | 2       |
| C#, `Shellwright.ConfigSchema.Tests` | 133     |
| **Total**                            | **328** |

Of the C# total, 87 are the cross-language contract: for each of the 29 fixtures,
the diagnostics, the canonical form, and all three cache keys must match the
committed goldens exactly.

### Sprint 02 — as built

| Task   | Deliverable                                     | Verified by                                                | Status                                                                    |
| ------ | ----------------------------------------------- | ---------------------------------------------------------- | ------------------------------------------------------------------------- |
| T-02.1 | Project skeleton, size and startup optimisation | `TC-S02-AND-001`, `TC-S02-PRF-002`                         | ✅ release APK 0.80 MB against a 12 MB budget                             |
| T-02.2 | Config loading and runtime model                | `TC-S02-AND-002`, `TC-S02-AND-003`, `TC-S02-PRF-003`       | ✅ two-phase parse, unknown keys ignored                                  |
| T-02.3 | WebView host with hardening                     | `TC-S02-AND-004`…`012`, `TC-S02-SEC-001`, `TC-S02-SEC-002` | ◐ hardening and routing unit-tested; the instrumented cases need a device |
| T-02.4 | Native chrome                                   | `TC-S02-AND-013`…`020`                                     | ◐ built and lint-clean; UI tests need an emulator                         |
| T-02.5 | Link routing engine                             | `TC-S02-AND-021`…`028`, `TC-S02-PRF-004`                   | ✅ 68 unit tests including the per-navigation budget                      |
| T-02.6 | Connectivity, offline page, error handling      | `TC-S02-AND-029`…`034`                                     | ◐ built; airplane-mode cases need a device                                |
| T-02.7 | Test suite                                      | `TC-S02-PRF-001`                                           | ◐ JVM suite green in CI; Espresso and Macrobenchmark need an emulator     |

Android suite: **68 tests**, 0 failures at the close of Sprint 02;
**123** after Sprint 03 added the two shared contract suites.

### Sprint 03 — as built

| Task   | Deliverable                                   | Verified by                                          | Status                                                                       |
| ------ | --------------------------------------------- | ---------------------------------------------------- | ---------------------------------------------------------------------------- |
| T-03.1 | SwiftPM package, `ShellCore`/`ShellApp` split | `TC-S03-IOS-001`, ADR 0005                           | ✅ `ShellCore` builds and tests on Linux, off metered minutes                |
| T-03.2 | Config loading and runtime model              | `TC-S03-IOS-002`, `TC-S03-IOS-003`, `TC-S03-PRF-003` | ✅ two-phase parse ported, unknown keys ignored, same fixtures as Android    |
| T-03.3 | `WKWebView` host with hardening               | `TC-S03-IOS-004`…`012`, `TC-S03-SEC-001`             | ◐ allowlist and ATS unit-tested; the device cases need hardware              |
| T-03.4 | Native chrome                                 | `TC-S03-IOS-013`…`020`                               | ◐ built; UI tests need a Mac and a simulator                                 |
| T-03.5 | Link routing engine                           | `TC-S03-IOS-021`…`028`, `TC-S03-PRF-004`             | ✅ shared corpus, identical decisions on both shells                         |
| T-03.6 | Authentication routing (`RT-08`)              | `TC-S03-SEC-002`                                     | ◐ `ASWebAuthenticationSession` wired; only a real provider can confirm it    |
| T-03.7 | Connectivity, offline page, error handling    | `TC-S03-IOS-029`…`034`                               | ◐ built; airplane-mode cases need a device                                   |
| T-03.8 | Codemagic pipeline                            | `TC-S03-BLD-001`                                     | ⏳ config in place at the repository root; needs one manual `ios-verify` run |
| T-03.9 | Store proof — TestFlight and Play internal    | `TC-S03-BLD-002`, `TC-S03-BLD-003`                   | ⏳ **the M1 kill gate**; blocked on enrolment and hardware                   |

iOS `ShellCore` suite: **48 tests**, 0 failures.

### Cross-implementation contracts

The count of behaviours implemented more than once, and what holds each one
together. This table is the honest measure of how much of the system is
duplicated on purpose.

| Behaviour              | Implementations               | Corpus                         | Cases |
| ---------------------- | ----------------------------- | ------------------------------ | ----- |
| Config validation      | TypeScript, C#                | `tests/fixtures/expected/`     | 29    |
| Link routing           | Kotlin, Swift                 | `tests/fixtures/routing/`      | 21    |
| Backtracking heuristic | TypeScript, C#, Kotlin, Swift | `tests/fixtures/regex-safety/` | 30    |

⚠️ The routing corpus caught a real defect on its first run: the iOS
backtracking defence had no effect at all and hung the suite. See
`SPRINT-03_REVIEW.md`.

### Sprint 04 — as built

| Task   | Deliverable                             | Verified by                                  | Status                                                            |
| ------ | --------------------------------------- | -------------------------------------------- | ----------------------------------------------------------------- |
| T-04.1 | Codegen architecture, `IFileSink`       | `TC-S04-GEN-001`, `TC-S04-GEN-002`, ADR 0006 | ✅ in-memory and directory sinks, duplicate paths rejected        |
| T-04.2 | Android project templating              | `TC-S04-GEN-003`…`018`                       | ✅ 55 files from any valid config, escaping proven to a built APK |
| T-04.3 | Asset pipeline (icons, splash, colours) | `TC-S04-GEN-019`…`028`                       | ❌ **not started** — moved whole to Sprint 05                     |
| T-04.4 | Determinism and normalisation           | `TC-S04-GEN-029`, `TC-S04-GEN-030`           | ✅ byte-identity per fixture, plus key-order invariance           |
| T-04.5 | Golden-file test infrastructure         | `TC-S04-GEN-031`, `TC-S04-GEN-032`           | ✅ 7 fixtures, 147 files, `tools/ApproveGolden`                   |
| T-04.6 | Nightly real-build verification         | `TC-S04-BLD-001`, `TC-S04-BLD-003`           | ✅ 7-fixture matrix, release build and size budget                |
|        | Emulator smoke test                     | `TC-S04-BLD-002`                             | ⏳ needs emulator time                                            |

Codegen suite: **89 tests**, 0 failures.

⚠️ Two of the sprint's three bugs were caught only by running Gradle on the
generated output — one by no unit test and no golden file, one by no fixture at
all. See `SPRINT-04_REVIEW.md`; it is the argument for the nightly real-build
job being required rather than optional.

Programme total: **660 tests** — 228 TypeScript (226 config-schema, 2 studio),
259 C#, 125 Kotlin, 48 Swift.

⚠️ Run the TypeScript suites through `pnpm test`, not `vitest` from the
repository root. The studio's two tests need the jsdom environment its own
`vitest.config.ts` sets, and a root-level run silently drops that config and
fails them with `document is not defined` — a failure in the invocation, not in
the code.
