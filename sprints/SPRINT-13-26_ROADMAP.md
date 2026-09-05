# Sprints 13–26 — Phase 2 & 3 Roadmap

Task-level detail for the remaining 14 sprints. **Expand each into a full sprint file (matching the S00–S12 format) at the start of the sprint before it**, not now — by week 27 the M3 findings will have changed several of these, and detailed plans written 6 months early are fiction.

Same conventions throughout: 2 weeks, 55 h capacity, **38 h of new work**, reserves untouched.

---

# Phase 2 — Beta (weeks 27–40)

## Sprint 13 — Device Preview

**Goal:** Streamed, interactive iOS and Android devices in the browser, so users can see their app without installing anything.

| ID     | Task                                                                                                     | Est. |
| ------ | -------------------------------------------------------------------------------------------------------- | ---- |
| T-13.1 | Appetize.io integration (buy, don't build — free tier ~100 min/mo)                                       | 8 h  |
| T-13.2 | Session manager: pooling, warm devices, idle reaping, per-plan concurrency and duration limits           | 8 h  |
| T-13.3 | Preview panel UI: rotate, dark-mode toggle, locale switch, network throttle, device model picker         | 7 h  |
| T-13.4 | Auto-preview on build success; "preview this config" without a full build where a cached artifact exists | 6 h  |
| T-13.5 | Remote web inspector attached to the previewed device (`PV-03`)                                          | 6 h  |
| T-13.6 | Metering and quota enforcement on preview minutes                                                        | 3 h  |

**Key decisions:** ⚠️ Buy Appetize for v1 per master spec §13.5. Self-hosted Android streaming (LiveKit + scrcpy) is deferred to S25+ because the Oracle ARM host cannot run emulators well; revisit only when preview minutes become a real cost line.

**⚠️ Watch for:** WebRTC adds 40–120 ms latency. Label the preview as a configuration preview, not a performance-testing surface, or users will file bugs about jank that is the stream's, not the app's.

**Test focus:** `TC-S13-PV-*` — session lifecycle, idle reaping, concurrency limits per plan, metering accuracy, graceful degradation when the provider is at capacity.

**Exit criteria:** a user can preview an Android and an iOS build in-browser; sessions are metered and enforced; provider outage produces a clear message, never a spinner.

---

## Sprint 14 — Signing & Credentials ⚠️ HIGHEST-RISK SPRINT

**Goal:** Hold, or better, _avoid holding_, customer signing material safely.

| ID     | Task                                                                                                                     | Est. |
| ------ | ------------------------------------------------------------------------------------------------------------------------ | ---- |
| T-14.1 | Threat model + ADR: delegation vs custody vs BYO                                                                         | 5 h  |
| T-14.2 | OpenBao (Vault fork) deployment, per-tenant transit keys, audit logging                                                  | 8 h  |
| T-14.3 | **Delegation path (default)**: customer-issued App Store Connect API key, scoped; Play service account with minimal role | 8 h  |
| T-14.4 | **Custody path (opt-in)**: certificate/keystore upload, encrypted storage, ephemeral injection, destruction after build  | 8 h  |
| T-14.5 | **BYO path**: produce an unsigned artifact for the customer to sign locally                                              | 4 h  |
| T-14.6 | Credential lifecycle: expiry monitoring, rotation reminders, revocation                                                  | 5 h  |

**⚠️ Non-negotiables (master spec §18.2):**

- Prefer **delegation** over custody; prefer **Play App Signing** so Google holds the app signing key and you only touch the upload key
- Key material never in Postgres, never in logs, never in artifacts
- Injected at job start, VM destroyed after
- Every access audit-logged with actor, app, job, timestamp
- Extend the S07/S08 redaction filter with Apple/Google-specific patterns, with a regression corpus
- **Publish the key-handling model publicly** — it becomes a sales asset

**Test focus:** `TC-S14-SEC-*` — this sprint gets the densest security test coverage in the programme. Negative tests dominate: key material must not appear in logs, artifacts, database dumps, error responses, or support exports. Add a test that greps every produced artifact and log for known test-key fingerprints.

**Exit criteria:** all three signing paths work; a full audit trail exists; an external review of the threat model is booked for S25.

---

## Sprint 15 — Publishing Automation

**Goal:** Submit to TestFlight and Play tracks from the studio, with the friction log from S03 turned into product.

| ID     | Task                                                                                                           | Est. |
| ------ | -------------------------------------------------------------------------------------------------------------- | ---- |
| T-15.1 | Publishing state machine: draft → uploading → processing → in review → live, resumable and notification-driven | 7 h  |
| T-15.2 | App Store Connect API: create app record, upload build, manage TestFlight groups, submit for review            | 9 h  |
| T-15.3 | Play Developer API: upload AAB, manage tracks (internal/closed/open/production), staged rollout                | 8 h  |
| T-15.4 | Guided publishing wizard built from the S03 friction log                                                       | 8 h  |
| T-15.5 | Submission status polling, webhooks, and email notifications                                                   | 6 h  |

**⚠️ Guideline 4.2.6 constraint (master spec §7.2):** the customer's own developer account submits, always. The wizard's job is to make _their_ submission easy, never to submit on their behalf. Build the UI so this is obvious rather than a footnote.

**Test focus:** `TC-S15-PUB-*` — state machine transitions including illegal ones, resumability across restarts, API error mapping (⚠️ Apple's and Google's error responses are notoriously unhelpful; map the top 20 to actionable messages), and the `CFBundleVersion` increment rule.

**Exit criteria:** an app goes from studio to TestFlight and Play internal track without leaving the browser, using the customer's own credentials.

---

## Sprint 16 — Store Readiness Score & Compliance Generators

**Goal:** Stop doomed submissions before they are made. This is differentiation pillar 5 and the strongest 4.2 mitigation available.

| ID     | Task                                                                                                      | Est. |
| ------ | --------------------------------------------------------------------------------------------------------- | ---- |
| T-16.1 | Readiness scoring engine: weighted rules over config, assets, and site analysis                           | 9 h  |
| T-16.2 | ⚠️ Hard block on submissions below threshold, with an explicit override that records who overrode and why | 4 h  |
| T-16.3 | Privacy manifest generator UI + validation (extends S05/S10 fragments)                                    | 6 h  |
| T-16.4 | Play Data Safety form generator (extends S10 fragments)                                                   | 6 h  |
| T-16.5 | Age rating questionnaire assistant; export compliance; DSA trader status guidance                         | 6 h  |
| T-16.6 | Rejection knowledge base: searchable, seeded from the S03 friction log and every alpha rejection          | 7 h  |

**Scoring dimensions** (derived from master spec §7.1):
native navigation present · push configured · offline handling · deep links · at least one native capability · site is responsive · no unjustified permissions · icon and splash quality · privacy declarations complete · store listing assets present.

**⚠️ The knowledge base is a permanent, compounding asset.** Every rejection any customer receives, with the reviewer's exact wording and the fix that worked, goes in. Nobody in this category maintains one properly. Start it now and it is unassailable in two years.

**Test focus:** `TC-S16-PUB-*` — score correctness against the fixture corpus (⚠️ `edge-single-page.json` must score below threshold), generator output validity against Apple's and Google's schemas, override audit trail.

**Exit criteria:** a bare wrapper config cannot be submitted without an explicit, recorded override; privacy manifest and Data Safety forms generate correctly.

---

## Sprint 17 — Billing, Metering & Quotas

**Goal:** Take money, and enforce the limits that the pricing model in master spec §17 depends on.

| ID     | Task                                                                       | Est. |
| ------ | -------------------------------------------------------------------------- | ---- |
| T-17.1 | Stripe integration: products, prices, subscriptions, customer portal       | 8 h  |
| T-17.2 | Metered usage reporting: iOS build minutes, preview minutes, storage       | 7 h  |
| T-17.3 | Quota engine: soft warnings at 80%, hard limits, ⚠️ one-time grace overage | 6 h  |
| T-17.4 | Usage dashboard for the customer; internal cost dashboard for you          | 6 h  |
| T-17.5 | Plan gating (⚠️ **only** the metered dimensions — never features)          | 5 h  |
| T-17.6 | Tax handling; evaluate Paddle as merchant-of-record vs own entity          | 6 h  |

**⚠️ Pricing enforcement rules from master spec §17.5:**

- No activation fees. Subscription only.
- Warn at 80%; **never hard-stop mid-submission**; allow one grace overage
- Gate compute, never capability — no watermark, no plugin caps, no seat caps, ever
- Grandfather early customers explicitly

**Sequencing note:** this is also the sprint where the free tiers start to run out. Expect to spend the first real money here (~$60/mo) — see the upgrade triggers in `02_FREE_RESOURCE_PLAYBOOK.md` §5.

**Test focus:** `TC-S17-API-*` — metering accuracy (⚠️ reconcile recorded usage against actual provider minutes; a drift over 2% is a bug), quota edge cases, webhook idempotency, proration, downgrade behaviour.

**Exit criteria:** a user can subscribe, consume metered resources, see accurate usage, and be billed correctly.

---

## Sprint 18 — Plugin Library Expansion (to 15)

**Goal:** Reach the launch plugin set. ⚠️ Master spec §15.2: **15 well, not 40 badly** — budget ~2 days/year of maintenance per SDK.

| Batch     | Plugins                                                                     | Est. |
| --------- | --------------------------------------------------------------------------- | ---- |
| Push      | first-party stub (real service in S20), OneSignal, Firebase Cloud Messaging | 9 h  |
| Auth      | social login (Google / Apple / Facebook)                                    | 7 h  |
| Commerce  | In-App Purchases (StoreKit 2 + Play Billing), RevenueCat                    | 9 h  |
| Native    | app review prompt, native datastore, share-into-app                         | 7 h  |
| Analytics | Crashlytics                                                                 | 6 h  |

(Existing from S10: haptics, QR scanner, biometrics. Total: 15.)

**⚠️ Hardest of these by a wide margin: In-App Purchases.** StoreKit 2 and Play Billing have completely different models, receipt validation is subtle, and getting it wrong loses customers' money. Give it the full 9 h and test against real sandbox accounts, not mocks.

**Test focus:** `TC-S18-PLG-*` — per-plugin contract fixtures (⚠️ mandatory per the S09 rule), all-pairs matrix now covering 15 plugins (~25 build combinations nightly), sandbox purchase flows on both stores.

**Exit criteria:** 15 plugins shipped with fixtures, docs, size deltas, and privacy fragments; all-pairs matrix green.

---

## Sprint 19 — CLI, Source Export & Config-as-Code ⚠️ M4 MILESTONE

**Goal:** Deliver differentiation pillar 2 — "you own your app" — and open the public beta.

| ID     | Task                                                                                                                                   | Est. |
| ------ | -------------------------------------------------------------------------------------------------------------------------------------- | ---- |
| T-19.1 | `@shellwright/cli`: `init`, `validate`, `build`, `preview`, `deploy`, `pull`, `push`                                                   | 9 h  |
| T-19.2 | Source export: complete buildable projects, README, `build.sh`, ⚠️ verified by a nightly test that builds an export on a clean machine | 7 h  |
| T-19.3 | Config-as-code: config in the customer's git repo, CI-triggered builds, environments                                                   | 7 h  |
| T-19.4 | Build API + webhooks for third-party CI                                                                                                | 5 h  |
| T-19.5 | PWA / TWA export target (`BD-15`) — free, and a genuine answer for users who fail 4.2                                                  | 6 h  |
| T-19.6 | Beta launch: pricing page, onboarding polish, status page, support workflow                                                            | 4 h  |

**⚠️ Source export must be genuinely usable, not a technicality.** The nightly test that clones an export onto a clean runner and builds it is what keeps this honest. If it ever fails, the "no lock-in" claim is false.

**Test focus:** `TC-S19-*` — CLI command coverage, export buildability on clean machines for both platforms, config-as-code round-trip fidelity, TWA validity.

**Exit criteria (M4 gate):** self-serve signup → configured app → store submission with **zero founder involvement**; billing live; 15 plugins; readiness score gating.

---

# Phase 3 — Commercial (weeks 41–54)

## Sprint 20 — First-Party Push Service

**Goal:** Own push. Master spec §8.2 — Median has no first-party push, and account sprawl across OneSignal/Firebase is a real customer pain.

| ID     | Task                                                           | Est. |
| ------ | -------------------------------------------------------------- | ---- |
| T-20.1 | APNs (HTTP/2, token auth) and FCM v1 delivery service          | 9 h  |
| T-20.2 | Device registration, token lifecycle, ⚠️ invalid-token pruning | 6 h  |
| T-20.3 | Segmentation, tags, and targeting                              | 7 h  |
| T-20.4 | Composer UI, scheduling, deep-link payloads                    | 8 h  |
| T-20.5 | Delivery and open analytics                                    | 5 h  |
| T-20.6 | Consent management and permission-prompt timing guidance       | 3 h  |

**⚠️ Design notes:** APNs and FCM are free — your only cost is compute, so be generous with the free allowance (100k/mo per master spec §17.3). Handle token invalidation properly or you will accumulate millions of dead tokens. ⚠️ Prompt timing matters enormously for opt-in rates: never prompt on first launch; provide a `sw.push.register()` the site calls at a meaningful moment.

**Test focus:** `TC-S20-SV-*` — delivery to real devices on both platforms, token pruning, targeting correctness, throughput under load, consent state transitions.

---

## Sprint 21 — First-Party Analytics & Crash Reporting

| ID     | Task                                                                                        | Est. |
| ------ | ------------------------------------------------------------------------------------------- | ---- |
| T-21.1 | ClickHouse deployment and event schema                                                      | 7 h  |
| T-21.2 | Ingest endpoint: batched, compressed, ⚠️ rate-limited, schema-validated                     | 7 h  |
| T-21.3 | Shell SDK: sessions, screen views, custom events, offline queueing                          | 7 h  |
| T-21.4 | Crash reporting with symbolication (⚠️ requires the dSYM/mapping retention from S08 T-08.5) | 9 h  |
| T-21.5 | Dashboards: DAU/MAU, retention cohorts, screen flow, crash-free rate                        | 8 h  |

**⚠️ Privacy first:** no IDFA, no cross-app tracking, no device fingerprinting. Declare exactly what is collected in the privacy manifest fragments. "Analytics that don't track your users" is a marketable position and it keeps you out of ATT prompts entirely.

**Test focus:** `TC-S21-SV-*` — ingest under load, event-loss rate on poor networks, symbolication correctness across shell versions, dashboard query performance at 100M rows.

---

## Sprint 22 — OTA Bundle Delivery

**Goal:** Ship web changes without a store review, safely and legally.

| ID     | Task                                                                                    | Est. |
| ------ | --------------------------------------------------------------------------------------- | ---- |
| T-22.1 | Bundle format, Ed25519 signing, manifest                                                | 7 h  |
| T-22.2 | Shell loader: verify, stage, apply on next launch, ⚠️ rollback on repeated boot failure | 9 h  |
| T-22.3 | CDN delivery on R2 (⚠️ zero egress — this is why OTA can be near-free)                  | 5 h  |
| T-22.4 | Staged rollout with automatic halt on crash-rate regression                             | 8 h  |
| T-22.5 | CLI integration and deployment history                                                  | 5 h  |
| T-22.6 | ⚠️ Compliance guardrails per master spec §8.4                                           | 4 h  |

**⚠️ Legal boundaries (master spec §8.4):** permitted for bug fixes, copy, layout, endpoint swaps, and feature-flag defaults; **not** for new feature areas, new paywalls, changed pricing, or unhiding admin surfaces. Build guardrails that make violations awkward — warn loudly when a bundle changes navigation structure or introduces a payment surface, and log every deployment for audit. Re-verify the current Apple DPLA §3.3.1(B) wording before shipping; the clause has moved twice.

**Test focus:** `TC-S22-SV-*` — signature verification (⚠️ an unsigned or mis-signed bundle must never execute), rollback on boot failure, staged rollout arithmetic, delta download correctness, offline behaviour with a staged bundle.

---

## Sprint 23 — Offline Engine

**Goal:** Layers 1 and 2 from master spec §13.9. ⚠️ Do not promise layer 3.

| ID     | Task                                                                                                                | Est. |
| ------ | ------------------------------------------------------------------------------------------------------------------- | ---- |
| T-23.1 | Shell offline: bundled skeleton, branded offline page, connectivity events (hardening what S02/S03 started)         | 6 h  |
| T-23.2 | Bundle fallback: serve the last known-good OTA bundle when the network is down                                      | 9 h  |
| T-23.3 | Offline download manager (`RT-11`): queue, resume, progress, storage budget                                         | 8 h  |
| T-23.4 | Native datastore offline mode with a sync-queue API over the bridge                                                 | 8 h  |
| T-23.5 | Documentation ⚠️ honestly stating the limits — no background sync guarantees on iOS, OEM battery killers on Android | 4 h  |

**⚠️ The honesty here is the differentiator.** Every competitor over-claims offline. Publishing a precise capability matrix, including what does _not_ work, builds more trust than a marketing claim and prevents a whole class of support ticket and refund.

**Test focus:** `TC-S23-*` — airplane-mode journeys, storage-budget enforcement, sync-queue replay ordering and idempotency, cache eviction under iOS storage pressure.

---

## Sprint 24 — Declarative Native Surfaces

**Goal:** Master spec §8.3A — the strongest possible Guideline 4.2 answer, generated from config.

| ID     | Task                                                             | Est. |
| ------ | ---------------------------------------------------------------- | ---- |
| T-24.1 | Surface schema design + ADR (⚠️ one-way door)                    | 6 h  |
| T-24.2 | Onboarding carousel: SwiftUI + Compose generation                | 8 h  |
| T-24.3 | Native settings screen bound to the native datastore             | 8 h  |
| T-24.4 | Native offline library screen                                    | 6 h  |
| T-24.5 | Studio editor for surfaces with live preview                     | 7 h  |
| T-24.6 | ⚠️ Enable by default for new apps; feed into the readiness score | 3 h  |

**⚠️ Constrain the DSL deliberately.** The temptation is to build a general-purpose UI language. Resist it: four surface types with fixed layouts and configurable content. A general DSL becomes a framework you must maintain forever, and it is not what customers are asking for.

**Test focus:** `TC-S24-*` — golden tests for generated SwiftUI/Compose, accessibility of generated surfaces (⚠️ VoiceOver/TalkBack traversal — this is the surface where you can genuinely beat the WebView boundary problem), size impact, readiness-score delta.

---

## Sprint 25 — Agency Workspace & Bulk Rebuild

**Goal:** Serve the highest-LTV segment (master spec §11) with the thing per-app licensing punishes.

| ID     | Task                                                                            | Est. |
| ------ | ------------------------------------------------------------------------------- | ---- |
| T-25.1 | Client sub-workspaces with per-client isolation and branding                    | 8 h  |
| T-25.2 | Bulk rebuild: rebuild N apps against a new toolchain, with a progress dashboard | 9 h  |
| T-25.3 | Config templates and inheritance (a base config plus per-client overrides)      | 8 h  |
| T-25.4 | White-label studio (custom domain, agency branding)                             | 7 h  |
| T-25.5 | Bulk operations: plugin enable/disable, version bump, config patch across apps  | 6 h  |

**⚠️ Bulk rebuild is the killer feature.** When Apple mandates a new SDK, an agency with 40 apps faces 40 manual rebuilds. One button doing all 40, with a per-app diff of what changed, is worth the entire Agency plan on its own — and it directly exploits the pricing weakness identified in master spec §9.

**Also in this sprint:** the **external security review** booked in S14, covering signing custody, the bridge (⚠️ especially Android `addJavascriptInterface`), tenant isolation, and OTA signing.

**Test focus:** `TC-S25-*` — cross-client isolation (⚠️ the RLS negative tests from S06, extended to sub-workspaces), bulk operation atomicity and partial-failure handling, template inheritance resolution order.

---

## Sprint 26 — Hardening, Performance, Security Audit & GA ⚠️ M5 MILESTONE

| ID     | Task                                                                                                      | Est. |
| ------ | --------------------------------------------------------------------------------------------------------- | ---- |
| T-26.1 | Act on the security review findings                                                                       | 8 h  |
| T-26.2 | Performance pass against every budget in `03_TEST_STRATEGY.md` §12                                        | 7 h  |
| T-26.3 | Reliability: chaos testing on the build pipeline, provider failover, graceful degradation                 | 7 h  |
| T-26.4 | Accessibility: full manual VoiceOver/TalkBack audit of shells and studio; publish a VPAT-style statement  | 6 h  |
| T-26.5 | SOC 2 Type I readiness: policies, evidence collection, access reviews                                     | 6 h  |
| T-26.6 | GA launch: pricing finalised against the S08 measured economics, docs complete, status page, support SLAs | 4 h  |

**M5 exit criteria:**

- 99.5% build success rate on valid configs (measured over 30 days)
- All performance budgets met
- Zero critical or high findings outstanding from the security review
- Accessibility statement published
- SOC 2 Type I evidence collection running
- Pricing reconciled with measured unit economics

---

# Cross-cutting reminders for Phases 2 and 3

| Recurring obligation                                                         | When                                                             |
| ---------------------------------------------------------------------------- | ---------------------------------------------------------------- |
| ⚠️ **OS treadmill** — new iOS/Android majors, new required target API levels | Reserve 25% of capacity permanently. Do not treat as a surprise. |
| Toolchain matrix (N and N−1 Xcode/AGP)                                       | Every sprint's nightly                                           |
| All-pairs plugin matrix                                                      | Nightly, grows with the catalogue                                |
| Cost review against `COSTS.md`                                               | Every retro                                                      |
| ⚠️ Free-tier limit re-verification                                           | Every sprint that first depends on one                           |
| Rejection knowledge base entries                                             | Every rejection any customer reports                             |
| Actual-vs-estimate ratio                                                     | Every retro; re-baseline if > 1.4                                |

# What is deliberately NOT in this plan

Per master spec §20: no website builder, no games support, no watchOS/tvOS/CarPlay/visionOS, no full offline-first sync engine as a marketing claim, no managed agency services business in year one. Every one of these will be requested. The answer is "not yet", and this line is the reason.
