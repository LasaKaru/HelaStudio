# Sprint 11 — App Studio I (Shell, Auth, Branding)

|                   |                                                           |
| ----------------- | --------------------------------------------------------- |
| **Weeks**         | 23–24                                                     |
| **Phase**         | 1 — Product                                               |
| **Capacity**      | 55 h (38 h new work)                                      |
| **Depends on**    | S06                                                       |
| **Blocks**        | S12                                                       |
| **Planned spend** | ~$12 (domain, if not already covered by the Student Pack) |

---

## 1. Sprint goal

Build the browser application: authentication, workspace and app management, and the branding/theme editors — the first surface a real user touches.

⚠️ **Performance is a feature here.** The studio is a large form-driven application with a live preview. If it lags while typing, users conclude the whole platform is slow. The budgets in `03_TEST_STRATEGY.md` §12 are enforced from this sprint.

---

## 2. Exit criteria

- [ ] Signup → email verification → login → create workspace → create app, all working
- [ ] App list, app detail, and settings pages
- [ ] Icon upload with automatic preview across all densities and adaptive-icon masks
- [ ] Splash and theme editors with live preview, including dark-mode variants
- [ ] Config saves produce new versions; version history visible
- [ ] Validation diagnostics rendered inline against the correct fields via JSON Pointer
- [ ] ⚠️ Initial JS < 200 KB gzipped; LCP < 2.0 s at 4× CPU throttle
- [ ] Accessible: full keyboard navigation, axe-core clean
- [ ] Coverage ≥ 70%

---

## 3. Task breakdown

| ID     | Task                                      | Est.     | Priority |
| ------ | ----------------------------------------- | -------- | -------- |
| T-11.1 | App shell, routing, design system         | 8 h      | P0       |
| T-11.2 | Auth flows                                | 6 h      | P0       |
| T-11.3 | Workspace and app management              | 6 h      | P0       |
| T-11.4 | Config state management and validation UX | 8 h      | P0       |
| T-11.5 | Branding editors (icon, splash, theme)    | 8 h      | P0       |
| T-11.6 | Performance and accessibility gates       | 2 h      | P0       |
|        | **Total**                                 | **38 h** |          |

---

## 4. Task detail

### T-11.1 — App shell, routing, design system (8 h)

**Stack:** React 19 + TypeScript + Vite + Tailwind + shadcn/ui + TanStack Router + TanStack Query.

**Steps:**

1. Vite with **route-level code splitting**. ⚠️ Monaco (~2 MB) and the JSON editor are lazy-loaded and must never appear in the initial chunk — assert this in the `size-limit` config, not by hoping.
2. TanStack Router with typed routes and file-based route definitions.
3. TanStack Query for all server state. ⚠️ **Never mirror server state into Zustand.** Zustand holds UI-only state (which panel is open, unsaved draft config). Mixing the two is the most common cause of stale-data bugs in dashboards like this.
4. Design system: build on shadcn/ui but establish tokens now — spacing scale, type scale, colour semantics (`--surface`, `--surface-elevated`, `--border`, `--danger`), radii, and motion durations. ⚠️ Retrofitting tokens after 40 components is a week of work.
5. Layout: persistent left nav (workspaces/apps), top bar (org switcher, user menu), content area, and a right-hand **preview panel slot** — reserved now, filled in S13.
6. Dark mode from the start. ⚠️ Adding it later means auditing every component; adding it now is nearly free.
7. Error boundary per route with a recoverable fallback; a global boundary for catastrophic failures.
8. Toast/notification system, and a `useConfirm()` hook for destructive actions.

**Acceptance criteria:** routes lazy-load; initial chunk under budget; dark mode complete; keyboard navigation works throughout.

**Tests:** `TC-S11-STU-001` … `TC-S11-STU-008`

---

### T-11.2 — Auth flows (6 h)

**Screens:** signup, login, email verification, forgot/reset password, OAuth callback, accept-invitation.

**Requirements:**

- ⚠️ **Never store tokens in `localStorage`.** The access token lives in memory; the refresh token is an `HttpOnly` cookie set by the API (S06). This is the difference between an XSS being an annoyance and an XSS being an account takeover.
- Silent refresh: a TanStack Query interceptor refreshes on 401 and retries once, with a single-flight guard so ten concurrent 401s trigger one refresh, not ten.
- ⚠️ Generic error messages on login — never reveal whether an email exists.
- Password strength meter using `zxcvbn` (lazy-loaded — it is large).
- OAuth with a `state` parameter and PKCE.
- Full keyboard operability and correct autocomplete attributes (`username`, `current-password`, `new-password`) so password managers work. Users notice when they do not.

**Acceptance criteria:** all flows work; token refresh is transparent and single-flight; tokens absent from `localStorage` and `sessionStorage` (asserted by test).

**Tests:** `TC-S11-STU-009` … `TC-S11-STU-018`, `TC-S11-SEC-001`

---

### T-11.3 — Workspace and app management (6 h)

**Screens:** workspace list, app list (grid with icon, name, platform status, last build), create-app wizard, app settings, member management.

**Create-app wizard — the highest-leverage screen in the product:**

1. **Enter URL.** Call the site-analysis endpoint (S06): mobile-friendliness, `manifest.json`, theme colour, favicon, title, viewport meta.
2. **Prefill everything possible** from that analysis — name, icon, theme colour, initial URL. ⚠️ The user should reach a configured app in under 60 seconds. Every field they must fill manually is a drop-off point.
3. Show detected problems immediately (no viewport meta, not responsive, HTTP-only) with links to fix guidance. Setting expectations early prevents the "my app looks broken" ticket later.
4. Bundle id suggested from the domain, editable, live-validated against the S01 rules.
5. Create → land directly in the branding editor with a preview.

**Acceptance criteria:** URL analysis prefills correctly for all three fixture sites; the wizard completes in under 60 seconds for a typical site; a non-responsive site produces a clear warning rather than a silent bad experience.

**Tests:** `TC-S11-STU-019` … `TC-S11-STU-028`

---

### T-11.4 — Config state management and validation UX (8 h)

⚠️ **The architectural core of the studio.** Get this right and every subsequent editor is trivial; get it wrong and each one fights the framework.

**Design:**

```
serverConfig (TanStack Query, source of truth)
      │
      ├─► draftConfig (Zustand, user's unsaved edits)
      │        │
      │        ├─► debounced 300ms ─► validate in a Web Worker ─► diagnostics
      │        └─► live preview
      │
      └─► save() → POST /config → new version → invalidate query
```

**Requirements:**

- ⚠️ **Validation runs in a Web Worker.** The full validator on `maximal.json` takes tens of milliseconds; running it on the main thread on every keystroke produces visible typing lag. The worker also lets you reuse the exact TypeScript validator from S01 rather than reimplementing rules.
- **JSON Pointer → field mapping.** Diagnostics carry a path like `/navigation/tabBar/items/2/url`; the form registry maps that to a specific input and renders the message inline. ⚠️ Build this mapping generically from the schema, not by hand — hand-mapping breaks every time the schema changes.
- **Unsaved-changes guard** on navigation, with a diff preview of what will be saved.
- **Optimistic save** with rollback on failure.
- **Autosave draft to `sessionStorage`** so a refresh does not lose work. ⚠️ Draft only, never the saved config, and never anything secret.
- **Uncontrolled forms** via `react-hook-form` — controlled inputs across a 200-field config cause measurable lag.
- Version history panel with diff view (reuse the API's diff endpoint) and one-click restore-as-new-version. ⚠️ Restore creates a _new_ version; it never mutates history.

**Acceptance criteria:** typing in any field is jank-free (no frame over 16 ms); diagnostics appear inline within 400 ms; refresh preserves the draft; restore creates a new version.

**Tests:** `TC-S11-STU-029` … `TC-S11-STU-042`, `TC-S11-PRF-001`

---

### T-11.5 — Branding editors (8 h)

**Icon editor:**

- Drag-and-drop upload with immediate client-side validation (dimensions, square, format) **before** upload — instant feedback beats a round trip.
- ⚠️ Live preview across: iOS home screen (rounded-rect mask), Android adaptive icon (circle, squircle, rounded-square masks), and the Android 13+ themed monochrome variant. The adaptive-icon safe zone must be visualised — showing the 66dp safe-zone overlay prevents the single most common icon mistake (content clipped on round launchers).
- Alpha-channel detection with an offer to flatten against a chosen background (⚠️ required for iOS).
- Show the generated density set so the user can see what will ship.

**Splash editor:** background colour (light/dark), logo placement, live preview at three device sizes with correct Android 12+ icon-inset behaviour so the preview matches reality.

**Theme editor:**

- Colour pickers for primary, accent, nav bar, tab bar, status bar, with light and dark variants.
- ⚠️ **Live WCAG contrast checking** on every foreground/background pair, with a visible AA/AAA badge. This is a genuine differentiator: no competitor surfaces accessibility at design time, and it feeds directly into the enterprise/public-sector story.
- "Extract palette from icon" as a one-click starting point — small feature, disproportionate delight.
- Preview rendered as a device frame showing the actual native chrome (tab bar, nav bar) with the chosen colours.

**Acceptance criteria:** icon upload → all previews render correctly within 1 s; safe-zone overlay accurate; contrast warnings correct against WCAG AA; palette extraction produces sensible colours.

**Tests:** `TC-S11-STU-043` … `TC-S11-STU-056`

---

### T-11.6 — Performance and accessibility gates (2 h)

1. `size-limit` in CI: initial chunk < 200 KB gzipped; ⚠️ a separate assertion that Monaco is not in the initial chunk.
2. Lighthouse CI on the app-list and branding-editor routes; fail below the budgets.
3. axe-core via Playwright on every route; fail on any serious or critical violation.
4. React Profiler check in CI for the config editor: typing must produce no render over 16 ms.

**Acceptance criteria:** all four gates pass and are wired into the PR pipeline.

**Tests:** `TC-S11-PRF-001` … `TC-S11-PRF-004`, `TC-S11-A11Y-001`

---

## 5. Test cases (selected detail)

| ID                | Type             | Precondition                                 | Steps                                       | Expected                                                      |
| ----------------- | ---------------- | -------------------------------------------- | ------------------------------------------- | ------------------------------------------------------------- |
| `TC-S11-SEC-001`  | Playwright       | Logged in                                    | Inspect `localStorage` and `sessionStorage` | No access or refresh token present                            |
| `TC-S11-STU-013`  | Playwright       | Session expired                              | Trigger 10 concurrent API calls             | One refresh request; all 10 succeed after retry               |
| `TC-S11-STU-021`  | Playwright       | Fixture `spa` site                           | Run create-app wizard                       | Name, theme colour, icon prefilled from the site              |
| `TC-S11-STU-024`  | Playwright       | Non-responsive test page                     | Enter its URL                               | Warning shown with remediation guidance                       |
| `TC-S11-STU-033`  | Component        | Config with an invalid tab URL               | Render editor                               | Error message appears inline on that specific tab's URL field |
| `TC-S11-STU-037`  | Playwright       | Unsaved edits                                | Navigate away                               | Guard prompts with a diff of pending changes                  |
| `TC-S11-STU-039`  | Playwright       | Unsaved edits                                | Reload the page                             | Draft restored from `sessionStorage`                          |
| `TC-S11-STU-041`  | Playwright       | Version history with v1–v5                   | Restore v2                                  | v6 created matching v2; v1–v5 unchanged                       |
| `TC-S11-STU-047`  | Component        | Icon with content outside the 66dp safe zone | Render adaptive preview                     | Clipping shown; warning displayed                             |
| `TC-S11-STU-052`  | Component        | `#777777` on `#FFFFFF`                       | Render theme editor                         | Contrast badge shows AA fail with the computed ratio          |
| `TC-S11-PRF-001`  | Profiler         | `maximal` config loaded                      | Type 50 characters into the app-name field  | No render exceeds 16 ms                                       |
| `TC-S11-PRF-002`  | size-limit       | Production build                             | Measure initial chunk                       | < 200 KB gzipped; Monaco absent                               |
| `TC-S11-A11Y-001` | Playwright + axe | Each route                                   | Run axe-core                                | Zero serious/critical violations                              |

---

## 6. Risks

| Risk                                                  | Likelihood | Impact   | Mitigation                                                                                                                                                                    |
| ----------------------------------------------------- | ---------- | -------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ Config state architecture chosen badly             | Medium     | **High** | T-11.4 is the load-bearing decision. Build it first, prove it with the branding editors, and only then build the navigation editors in S12.                                   |
| Bundle size creeps past budget                        | **High**   | Medium   | `size-limit` enforced from the first PR, not added later. Lazy-load anything over 50 KB.                                                                                      |
| Live preview implemented as a fake and misleads users | Medium     | Medium   | ⚠️ Previews here are _illustrative_ device frames. The **real** streamed device preview is S13. Label them honestly in the UI ("approximate preview") so nobody is surprised. |
| Design-system time sink                               | **High**   | Medium   | Use shadcn/ui as-is. Customise tokens only. No bespoke components this sprint.                                                                                                |
| Accessibility treated as later work                   | Medium     | Medium   | axe-core gate from day one; it is far cheaper than an audit at S26                                                                                                            |

---

## 7. Deliverables

- `apps/studio` — React application with auth, workspaces, apps, and branding editors
- Config state architecture with Web Worker validation and generic JSON-Pointer diagnostic mapping
- Create-app wizard with site analysis and prefill
- Icon/splash/theme editors with adaptive-icon safe zones and WCAG contrast checking
- Performance, bundle-size, and accessibility gates in CI
- `SPRINT-11_REVIEW.md`
