# Sprint 12 — App Studio II & Private Alpha ⚠️ M3 MILESTONE

|                   |                      |
| ----------------- | -------------------- |
| **Weeks**         | 25–26                |
| **Phase**         | 1 — Product          |
| **Capacity**      | 55 h (38 h new work) |
| **Depends on**    | S07, S08, S10, S11   |
| **Blocks**        | Phase 2              |
| **Planned spend** | $0–$20               |

---

## 1. Sprint goal

Complete the studio — navigation, link rules, plugin configuration, build UI, raw JSON editor — and put it in front of **10 real external users**.

⚠️ **M3 milestone gate.** The measure of this sprint is not features shipped; it is whether ten strangers can independently create, configure, build, and install an app.

---

## 2. Exit criteria

- [ ] Navigation editor: drag-and-drop tabs and drawer items with icon picker
- [ ] Link-rule editor with a live tester
- [ ] Plugin catalogue with per-plugin config forms **generated from each plugin's `configSchema`**
- [ ] Build UI: trigger, live logs, artifact download, QR-to-device install
- [ ] Raw JSON editor (Monaco) with schema autocomplete, bidirectionally synced with the visual editors
- [ ] Web-worker validation, inline diagnostics, and per-plugin size deltas all visible
- [ ] ⚠️ **10 external alpha users onboarded; activation measured**
- [ ] Docs site live with quickstart, bridge reference, and plugin reference

---

## 3. Task breakdown

| ID     | Task                                            | Est.     | Priority |
| ------ | ----------------------------------------------- | -------- | -------- |
| T-12.1 | Navigation editor                               | 7 h      | P0       |
| T-12.2 | Link-rule editor with tester                    | 5 h      | P0       |
| T-12.3 | Plugin catalogue and schema-driven config forms | 7 h      | P0       |
| T-12.4 | Build UI with live logs and device install      | 7 h      | P0       |
| T-12.5 | Raw JSON editor with two-way sync               | 5 h      | P0       |
| T-12.6 | Docs site                                       | 4 h      | P0       |
| T-12.7 | Alpha onboarding and instrumentation            | 3 h      | P0       |
|        | **Total**                                       | **38 h** |          |

---

## 4. Task detail

### T-12.1 — Navigation editor (7 h)

**Tab bar editor:** drag-to-reorder (`dnd-kit`), per-item label / icon / URL / active-pattern, live preview in the device frame, ⚠️ warning above 5 tabs (iOS collapses the rest into "More"), and a per-item toggle for visibility rules.

**Drawer editor:** same, plus nesting one level, section headers, and dividers.

**Icon picker:** bundled icon set (Lucide, ~1,400 icons, MIT) with search, plus custom SVG/PNG upload. ⚠️ **Lazy-load the icon set** — 1,400 icons must not be in the initial bundle. Render a virtualised grid; a naive grid of 1,400 nodes janks badly.

**Nav-bar editor:** title source (document title vs static vs per-URL), action buttons with icons and bridge callbacks, back-affordance behaviour.

⚠️ **Reordering must produce a stable, minimal config diff.** Each item carries the `id` mandated by the S01 schema; without stable ids, a reorder rewrites the whole array and every diff is unreadable.

**Acceptance criteria:** drag-reorder works with keyboard as well as pointer (accessibility); preview updates within 100 ms; >5 tabs warns; diffs are minimal.

**Tests:** `TC-S12-STU-001` … `TC-S12-STU-012`

---

### T-12.2 — Link-rule editor with tester (5 h)

**Editor:** ordered list of rules, each with a pattern, an action, and an optional note; drag-to-reorder (⚠️ order is semantic — first match wins); inline regex validation with a plain-English explanation of what the pattern matches.

**The tester — the feature that makes this comprehensible:**

- A URL input; on entry, show **which rule matched, in what position, and the resulting action**.
- Show shadowed rules (a later rule that can never match because an earlier one is broader) with a warning, matching the `CFG_LINK_RULE_UNREACHABLE` diagnostic from S01.
- Pre-populate with URLs discovered during site analysis so the tester is useful immediately rather than empty.
- ⚠️ Run matching in the Web Worker with a timeout, so a catastrophic user regex cannot freeze the tab.

Link routing is the single most confusing part of these platforms for users. An interactive tester converts a support ticket into a self-service moment, and it costs half a day.

**Acceptance criteria:** tester correctly reports the matching rule for all fixture URLs; shadowed rules flagged; a catastrophic regex does not freeze the UI.

**Tests:** `TC-S12-STU-013` … `TC-S12-STU-020`

---

### T-12.3 — Plugin catalogue and schema-driven config forms (7 h)

**Catalogue:** grid of plugins by category, with description, platform support badges, ⚠️ **binary size delta** (from S10), required permissions, and whether a third-party licence is needed. Toggle to enable.

⚠️ **The forms must be generated from each plugin's `configSchema`, not hand-written.** With 15 plugins arriving in S18 and more later, hand-written forms do not scale and they drift from the manifests. Build a generic JSON-Schema form renderer supporting: string, number, boolean, enum (select), array of enum (multi-select), object (fieldset), with `title` and `description` from the schema rendering as label and help text. This is why S01 mandated descriptions on every property.

**Also required:**

- Conflict warnings surfaced live from the S10 detector as the user toggles — before saving, not after.
- Running total of estimated app size, with a visible budget bar.
- Per-plugin "what this adds to your app" summary: permissions, plist keys, size, and privacy declarations. ⚠️ Transparency here is a trust differentiator — competitors hide it, and users who get store-rejected for an unjustified permission never find out why.

**Acceptance criteria:** all three S10 plugins configure through generated forms with no bespoke code; enabling two conflicting plugins warns immediately; size total updates live.

**Tests:** `TC-S12-STU-021` … `TC-S12-STU-032`

---

### T-12.4 — Build UI with live logs and device install (7 h)

**Build panel:**

- Platform selector, build-type selector (debug/release), and the resolved config version being built
- Trigger with an idempotency key generated client-side (⚠️ prevents double-click double-builds, matching S07's API contract)
- **Live log viewer**: WebSocket-fed, **virtualised** (⚠️ a 50 MB log will destroy a naive list), with filtering by severity, search, auto-scroll with a pause-on-manual-scroll, and a "jump to first error" button
- Progress with the real state-machine stage names from S07, plus queue position when waiting for macOS capacity
- Cancel button wired to the S07 cancellation path
- **Artifact download** via signed URL, plus a **QR code** for direct device install — ⚠️ for Android this is the fastest possible "see it on your phone" loop and it is the moment alpha users decide whether they believe in the product
- Build history with config diff versus the previous build, so "what changed?" is always answerable

⚠️ **Failure presentation matters more than success presentation.** When a build fails, show the typed diagnostic from S08 prominently at the top with its remediation, and put the raw log behind a disclosure. Never present a wall of Gradle output as the primary answer.

**Acceptance criteria:** build triggers, streams, completes, and downloads; cancellation works from the UI; a 50 MB log scrolls smoothly; a failed build shows the actionable diagnostic first.

**Tests:** `TC-S12-STU-033` … `TC-S12-STU-046`, `TC-S12-PRF-001`

---

### T-12.5 — Raw JSON editor with two-way sync (5 h)

- Monaco, **lazy-loaded**, wired to the hosted JSON Schema from S01 for autocomplete, hover documentation, and inline validation.
- ⚠️ **Two-way sync:** visual edits update the JSON; JSON edits update the visual editors. The sync is the hard part. Make the **draft config the single source of truth**, with both editors as views over it. Never let the two hold independent copies — that is the classic dual-editor bug where changes silently vanish.
- Show the canonicalised form on demand, and the resolved-with-defaults form as a read-only view. Users repeatedly ask "what is actually being applied?"; answering it is cheap.
- Copy config, download config, and import config (validated on paste).
- Diff view against the last saved version.

**Acceptance criteria:** edits propagate both ways with no loss; schema autocomplete works; Monaco is absent from the initial bundle (asserted).

**Tests:** `TC-S12-STU-047` … `TC-S12-STU-056`

---

### T-12.6 — Docs site (4 h)

Astro Starlight on Cloudflare Pages (free). Minimum content for alpha:

1. **Quickstart** — URL to installed app in ten minutes
2. **Configuration reference** — generated from the JSON Schema descriptions, so it can never drift
3. **Bridge reference** — generated from TSDoc
4. **Plugin reference** — generated from manifests
5. **Publishing guide** — from the S03 friction log
6. **Troubleshooting** — seeded with the diagnostic code table from S01

⚠️ **Generate three of the six from source.** Hand-written reference documentation is stale within two sprints; generated documentation is correct by construction and costs nothing to maintain.

**Acceptance criteria:** docs live, searchable, and generated sections match current source.

**Tests:** `TC-S12-DOC-001`, `TC-S12-DOC-002`

---

### T-12.7 — Alpha onboarding and instrumentation (3 h)

**Recruit 10 users** deliberately across segments: 3 indie/AI-builder, 3 SaaS teams, 2 agencies, 2 ecommerce. ⚠️ Recruit from communities where they already are — the Lovable/Bolt/Replit Discords, Indie Hackers, r/reactnative, and relevant Slack groups. Offer free lifetime access in exchange for a 30-minute call.

**Instrument the activation funnel** (a simple event table in Postgres is enough at this scale — do not build analytics infrastructure yet):

| Step                  | Event                 | Target           |
| --------------------- | --------------------- | ---------------- |
| Signed up             | `user.signed_up`      | 100%             |
| Created an app        | `app.created`         | > 80%            |
| Saved a config change | `config.saved`        | > 70%            |
| Triggered a build     | `build.started`       | > 60%            |
| Build succeeded       | `build.succeeded`     | > 90% of started |
| Installed on a device | `artifact.downloaded` | > 50%            |

⚠️ **Watch three users complete onboarding over a screen share, in silence.** Do not help them. The urge to help is overwhelming and it destroys the data. Write down every hesitation. This is worth more than any survey.

**Acceptance criteria:** 10 users onboarded; funnel instrumented and reported; three observation sessions completed and written up.

**Tests:** `TC-S12-E2E-001`

---

## 5. Test cases (selected detail)

| ID               | Type       | Precondition                             | Steps                                                                                       | Expected                                                                  |
| ---------------- | ---------- | ---------------------------------------- | ------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------- |
| `TC-S12-E2E-001` | Playwright | Fresh account                            | Sign up → create app from a URL → change theme → enable a plugin → build Android → download | Completes end to end; APK is valid and installable                        |
| `TC-S12-STU-006` | Playwright | 3 tabs configured                        | Reorder tabs by keyboard only                                                               | Order changes; diff shows only the reorder                                |
| `TC-S12-STU-010` | Playwright | 6 tabs configured                        | Save                                                                                        | Warning shown; save still permitted                                       |
| `TC-S12-STU-016` | Component  | `maximal` link rules                     | Enter an external URL in the tester                                                         | Correct rule index and action reported                                    |
| `TC-S12-STU-018` | Component  | Rules where rule 3 is shadowed by rule 1 | Open editor                                                                                 | Rule 3 flagged unreachable                                                |
| `TC-S12-STU-025` | Playwright | Plugin catalogue open                    | Enable two conflicting plugins                                                              | Immediate warning; save blocked with `CFG_PLUGIN_CONFLICT`                |
| `TC-S12-STU-028` | Component  | qr-scanner has a `formats` enum array    | Render its config form                                                                      | Multi-select generated from schema with descriptions as help text         |
| `TC-S12-STU-038` | Playwright | Build running                            | Click cancel                                                                                | Build cancelled within 5 s; UI reflects it                                |
| `TC-S12-STU-041` | Playwright | Build failed on a signing error          | View result                                                                                 | Typed diagnostic and remediation shown first; raw log behind a disclosure |
| `TC-S12-STU-050` | Playwright | Config open in both editors              | Edit app name in JSON, switch to visual                                                     | Visual editor shows the new name; no loss                                 |
| `TC-S12-PRF-001` | Playwright | 50 MB log archive                        | Open log viewer, scroll to end                                                              | Smooth scrolling; memory stable (virtualised)                             |
| `TC-S12-PRF-002` | size-limit | Production build                         | Measure initial chunk                                                                       | Monaco and the icon set both absent                                       |

---

## 6. Risks

| Risk                                                          | Likelihood | Impact   | Mitigation                                                                                                                                                                    |
| ------------------------------------------------------------- | ---------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ Alpha users cannot complete onboarding unaided             | **Medium** | **High** | This is exactly what the milestone tests. A low activation rate is a **finding**, not a failure — but it must change the next sprint's plan rather than be rationalised away. |
| Two-way JSON sync loses edits                                 | Medium     | High     | Single-source-of-truth architecture; explicit test for every sync direction                                                                                                   |
| Build UI is where all remaining pipeline bugs surface at once | **High**   | Medium   | Expect it. Reserve the sprint's rework buffer for it rather than adding scope.                                                                                                |
| Bundle budget blown by Monaco and the icon set                | **High**   | Medium   | Both lazy-loaded and both explicitly asserted absent from the initial chunk                                                                                                   |
| Alpha recruitment is slow                                     | Medium     | Medium   | ⚠️ Start recruiting in **week 23**, during Sprint 11, not on the last day of Sprint 12                                                                                        |

---

## 7. Deliverables

- Complete App Studio: navigation, link rules with tester, plugin catalogue with generated forms, build UI, JSON editor
- Docs site with three generated reference sections
- 10 alpha users onboarded with an instrumented activation funnel
- Three recorded, written-up onboarding observation sessions
- `SPRINT-12_REVIEW.md` — **M3 milestone gate assessment with the activation numbers**

---

## 8. ⚠️ M3 gate questions

Answer in `SPRINT-12_REVIEW.md` before planning Sprint 13:

1. What percentage of the 10 users created an app without help? (target > 80%)
2. What percentage got a successful build? (target > 60%)
3. What percentage installed it on a device? (target > 50%)
4. What was the single most common point of confusion?
5. What did users ask for that is not in the roadmap?
6. Actual-to-estimate ratio across S00–S12 — re-baseline the remaining 14 sprints if > 1.4.
7. **Does the next sprint plan reflect the answers above, or is it just the original plan?**

Question 7 is the one that matters.
