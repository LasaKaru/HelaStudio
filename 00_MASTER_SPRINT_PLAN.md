# Master Sprint Plan

**Programme:** Shellwright web-to-native platform
**Cadence:** 2-week sprints
**Team:** 1 engineer (you), part-time
**Assumed capacity:** **55 productive hours per sprint** (~27.5 h/week). Adjust the whole plan by scaling — do not compress scope silently.
**Total:** 27 sprints ≈ 54 weeks ≈ 12.5 months to GA

---

## 1. Capacity model

|                                                     | Hours   |
| --------------------------------------------------- | ------- |
| Nominal per sprint                                  | 55      |
| Reserved: bug fix / rework carry-over               | 8 (15%) |
| Reserved: ops, dependency bumps, store/OS treadmill | 5 (9%)  |
| Reserved: documentation                             | 4 (7%)  |
| **Available for new feature work**                  | **38**  |

Every sprint below is scoped to **≤ 38 hours of new work**. If a sprint's task estimates exceed 38, cut scope — never the reserves. The reserves are what stop a solo programme from collapsing in month four.

**Estimation rule:** estimate in hours, at the task level, before the sprint starts. Multiply any estimate involving Apple tooling, code signing, or a third-party SDK by **2×**. This is not pessimism; it is the observed constant in mobile build engineering.

---

## 2. Sprint index

### Phase 0 — Proof (weeks 1–8) — **kill gate at end of S03**

| #   | Sprint                             | Goal                                                                  | New-work est. |
| --- | ---------------------------------- | --------------------------------------------------------------------- | ------------- |
| S00 | Foundations & Dev Environment      | Monorepo, CI, standards, free infra provisioned, test harness         | 36 h          |
| S01 | Config Schema & Validation Engine  | `appconfig.json` v1 schema, validator, migration framework            | 37 h          |
| S02 | Android Shell MVP                  | Hand-written Kotlin shell driven by config: WebView, tab bar, nav bar | 38 h          |
| S03 | iOS Shell MVP + Manual Store Proof | Swift shell parity + one app manually on TestFlight and Play internal | 38 h          |

### Phase 1 — Pipeline (weeks 9–18)

| #   | Sprint                        | Goal                                                              | New-work est. |
| --- | ----------------------------- | ----------------------------------------------------------------- | ------------- |
| S04 | Codegen Engine — Android      | Config → complete buildable Gradle project                        | 37 h          |
| S05 | Codegen Engine — iOS          | Config → complete buildable Xcode project                         | 38 h          |
| S06 | Control Plane API             | Auth, orgs, workspaces, apps, config versions                     | 37 h          |
| S07 | Build Orchestration           | Temporal workflows + Linux runner + Android cloud builds          | 38 h          |
| S08 | macOS Runner & Artifact Store | iOS cloud builds via Codemagic/self-hosted, R2 artifacts, caching | 38 h          |

### Phase 1 — Product (weeks 19–26) — **private alpha at end of S12**

| #   | Sprint                | Goal                                                                   | New-work est. |
| --- | --------------------- | ---------------------------------------------------------------------- | ------------- |
| S09 | Bridge Protocol & SDK | Versioned JS↔native bridge, npm package, capability negotiation       | 37 h          |
| S10 | Plugin System         | Manifest format, build-time injection, 3 plugins                       | 38 h          |
| S11 | App Studio I          | React SPA, auth, app list, branding + theme editors, live preview stub | 38 h          |
| S12 | App Studio II         | Navigation editor, link rules, plugin config, build UI, JSON editor    | 38 h          |

### Phase 2 — Beta (weeks 27–40) — **public beta at end of S19**

| #   | Sprint                                        | Goal |
| --- | --------------------------------------------- | ---- |
| S13 | Device Preview                                |
| S14 | Signing & Credentials                         |
| S15 | Publishing Automation                         |
| S16 | Store Readiness Score & Compliance Generators |
| S17 | Billing, Metering & Quotas                    |
| S18 | Plugin Library Expansion (to 15)              |
| S19 | CLI, Source Export & Config-as-Code           |

### Phase 3 — Commercial (weeks 41–54) — **GA at end of S26**

| #   | Sprint                                     | Goal |
| --- | ------------------------------------------ | ---- |
| S20 | First-Party Push Service                   |
| S21 | First-Party Analytics & Crash Reporting    |
| S22 | OTA Bundle Delivery                        |
| S23 | Offline Engine                             |
| S24 | Declarative Native Surfaces                |
| S25 | Agency Workspace & Bulk Rebuild            |
| S26 | Hardening, Performance, Security Audit, GA |

---

## 3. Dependency graph

```
S00 ──┬──► S01 ──┬──► S02 ──┐
      │          │          ├──► S03 ──┬──► S04 ──┐
      │          └──────────┘          │          ├──► S07 ──► S08 ──┐
      └──────────────────────► S06 ────┘   S05 ───┘                  │
                                                                      │
                          S09 ◄──────────────────────────────────────┘
                           │
                           ├──► S10 ──┬──► S12 ──► [ALPHA]
                           │          │
                        S11 ──────────┘
                                       │
   [ALPHA] ──► S13 ──► S14 ──► S15 ──► S16 ──► S17 ──► S18 ──► S19 ──► [BETA]
                                       │
   [BETA] ──► S20 ──► S21 ──► S22 ──► S23 ──► S24 ──► S25 ──► S26 ──► [GA]
```

**Critical path:** S00 → S01 → S02 → S03 → S04 → S07 → S08 → S09 → S10 → S12.
S05, S06, S11 have float and can absorb slippage. Do not let S07/S08 slip — everything downstream sits on the build pipeline.

---

## 4. Milestones & gates

| Milestone              | End of | Gate criteria (all must be true)                                                                                                                    |
| ---------------------- | ------ | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| **M1 — Proof**         | S03    | A config JSON produces an app installed from TestFlight and Play internal testing on a physical device. Both shells hand-built. Total spend ≤ $124. |
| **M2 — Pipeline**      | S08    | A config submitted via API produces signed AAB + IPA on cloud runners with no manual step. p95 Android build < 6 min, iOS < 15 min.                 |
| **M3 — Private Alpha** | S12    | 10 external users create an app in the browser, configure it, build it, and install it. Bridge SDK published to npm. 3 plugins working.             |
| **M4 — Public Beta**   | S19    | Self-serve signup → configured app → store submission with no founder involvement. Billing live. 15 plugins. Readiness Score gating submissions.    |
| **M5 — GA**            | S26    | First-party push, analytics, OTA, offline, native surfaces, agency tier. Security audit passed. 99.5% build success on valid configs.               |

**Kill gate at M1.** If Sprint 03 overruns by more than 4 weeks, the mobile build treadmill is heavier than this plan assumes and the programme should be re-scoped (e.g. Android-only launch) rather than continued at the same shape.

---

## 5. Ceremonies (solo-adapted)

| When                            | What                                                                                                                   | Time   |
| ------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | ------ |
| Sprint day 1, morning           | **Planning.** Re-read the sprint file. Confirm estimates against actual remaining capacity. Cut scope now, not later.  | 60 min |
| Every working day, first 10 min | **Standup-to-self.** Write 3 lines in `JOURNAL.md`: done / today / blocked. Blocked >2 days = escalate to a scope cut. | 10 min |
| Sprint day 7                    | **Mid-sprint checkpoint.** If < 40% of estimated hours are burned down, cut the lowest-priority task now.              | 30 min |
| Sprint last day                 | **Review.** Demo to yourself against exit criteria. Record actual hours per task.                                      | 60 min |
| Sprint last day                 | **Retro.** One thing to keep, one to change. Update the estimate multiplier if you were consistently off.              | 30 min |

**Velocity calibration:** after S03, compute `actual_hours / estimated_hours` across all completed tasks. Apply that ratio to every subsequent estimate in this plan. If it is > 1.4, re-baseline the whole schedule before continuing.

---

## 6. Definition of Ready (a task may not be started unless)

- [ ] It has an ID and an hour estimate
- [ ] Its acceptance criteria are written and testable
- [ ] Its test case IDs are listed
- [ ] Its dependencies are complete
- [ ] It fits in one sprint (if not, split it)

## 7. Definition of Done (a task is not done until)

- [ ] Code merged to `main` via PR, even solo — PRs are your review record
- [ ] All listed test cases pass in CI
- [ ] Coverage gate met (see `03_TEST_STRATEGY.md`)
- [ ] No new linter/analyser warnings
- [ ] No new `TODO` without a tracked issue number
- [ ] Public API changes documented in `/docs`
- [ ] `CHANGELOG.md` updated
- [ ] Secrets scan clean (gitleaks in CI)
- [ ] If it touches the build pipeline: a real APK/IPA was produced and installed on a physical device

## 8. Definition of Done (sprint level)

- [ ] All exit criteria in the sprint file are demonstrably met
- [ ] `SPRINT-NN_REVIEW.md` written: what shipped, actual vs estimated hours, what slipped and where it moved to
- [ ] Traceability matrix updated
- [ ] Main branch is deployable
- [ ] Cost report: actual spend this sprint vs planned

---

## 9. Branching & release

```
main            ← always deployable, protected, CI-gated
 └─ feat/S07-temporal-workflows      ← one branch per task or tight task group
 └─ fix/S07-build-timeout
 └─ chore/S07-bump-agp
```

- **Conventional Commits** (`feat:`, `fix:`, `chore:`, `perf:`, `test:`, `docs:`, `refactor:`). Drives automated changelog and semver.
- **Squash merge only.** One task = one commit on `main`.
- Tag each sprint end: `sprint-07`. Tag each milestone: `v0.1.0-alpha`, `v0.2.0-beta`, `v1.0.0`.
- **Shell versions are semver'd independently** (`shell-android@1.4.0`) because generated apps pin to a shell version and must be reproducible forever.

---

## 10. Risk burn-down schedule

Retire the biggest risks earliest. The order of Phase 0 is chosen for exactly this reason.

| Risk                                                  | Retired by | How                                                                  |
| ----------------------------------------------------- | ---------- | -------------------------------------------------------------------- |
| "iOS builds are impossible without a Mac I don't own" | S03        | Codemagic free tier + student account produces a real IPA            |
| "Apple will reject a webview app outright"            | S03        | Get one through TestFlight review, then App Store review             |
| "Codegen for two platforms is unmaintainable"         | S05        | Both generators working from one schema                              |
| "Build orchestration will be a swamp"                 | S07        | Temporal doing retries/cancellation/log streaming                    |
| "Mac build costs will exceed revenue"                 | S08        | Measured cost-per-build with caching on                              |
| "Plugin combinatorics will explode"                   | S10        | Manifest-driven injection proven with 3 plugins + conflict detection |
| "Nobody wants this"                                   | S12        | 10 alpha users, measured activation                                  |

---

## 11. Cost plan by phase

| Phase   | Sprints | Planned spend | Notes                                                                       |
| ------- | ------- | ------------- | --------------------------------------------------------------------------- |
| Phase 0 | S00–S03 | **$124**      | Apple Developer $99/yr + Google Play $25 one-time. Everything else free.    |
| Phase 1 | S04–S12 | **$0–$40**    | Free tiers hold. Possible domain ($12) and small Codemagic overage.         |
| Phase 2 | S13–S19 | **~$60/mo**   | First paid VPS + Appetize + managed Postgres as free tiers are outgrown.    |
| Phase 3 | S20–S26 | **~$250/mo**  | Mac host, larger VPS, ClickHouse, Sentry. Should be revenue-covered by now. |

Full detail and upgrade triggers: `02_FREE_RESOURCE_PLAYBOOK.md`.
