# Sprint 02 — Android Shell MVP

|                   |                                     |
| ----------------- | ----------------------------------- |
| **Weeks**         | 5–6                                 |
| **Phase**         | 0 — Proof                           |
| **Capacity**      | 55 h (38 h new work)                |
| **Depends on**    | S01                                 |
| **Blocks**        | S04, S09                            |
| **Planned spend** | $25 (Google Play Console, one-time) |

---

## 1. Sprint goal

Hand-write the Android shell: a real Kotlin app that reads an embedded `appconfig.json` and renders a native tab bar, native top bar, and a WebView, with the startup performance budget met.

**This is the product.** Everything else in the platform exists to generate and deliver this app. Write it carefully, by hand, and do not generate it yet — S04 will teach the generator to produce what you built here.

---

## 2. Exit criteria

- [ ] APK installs on a physical Android device and loads the `simple` fixture site
- [ ] Bottom tab bar, top app bar with dynamic title, and pull-to-refresh all driven by config
- [ ] Link rules route correctly: internal in-WebView, external to Chrome Custom Tabs
- [ ] Offline page shows on connectivity loss and recovers on reconnect
- [ ] Swapping the embedded `appconfig.json` changes the app with no code change
- [ ] ⚠️ Cold start to first frame < 300 ms, interactive < 500 ms, measured by Macrobenchmark
- [ ] APK (arm64 split) < 12 MB
- [ ] Espresso smoke suite green on an emulator in CI

---

## 3. Task breakdown

| ID     | Task                                                          | Est.     | Priority |
| ------ | ------------------------------------------------------------- | -------- | -------- |
| T-02.1 | Project skeleton, build config, size and startup optimisation | 6 h      | P0       |
| T-02.2 | Config loading and runtime model                              | 4 h      | P0       |
| T-02.3 | WebView host with hardening                                   | 8 h      | P0       |
| T-02.4 | Native chrome: tab bar, app bar, pull-to-refresh              | 7 h      | P0       |
| T-02.5 | Link routing engine                                           | 5 h      | P0       |
| T-02.6 | Connectivity, offline page, error handling                    | 4 h      | P0       |
| T-02.7 | Test suite: unit, Espresso, Macrobenchmark                    | 4 h      | P0       |
|        | **Total**                                                     | **38 h** |          |

---

## 4. Task detail

### T-02.1 — Project skeleton and build optimisation (6 h)

**Steps:**

1. Create `shells/android` — **a public repo** (see S00 T-00.2) so GitHub Actions macOS/Linux minutes stay unmetered.
2. Gradle setup with version catalogs (`libs.versions.toml`) — ⚠️ every dependency version pinned exactly, no ranges. Reproducibility is a hard requirement (`01_ENGINEERING_STANDARDS.md` anti-patterns).
3. `minSdk 24`, `targetSdk` current, Kotlin 2.x, Compose for native surfaces, Views for the WebView host.
4. **Startup and size optimisation, configured now rather than retrofitted:**
   ```kotlin
   // build.gradle.kts (app)
   android {
     buildTypes {
       release {
         isMinifyEnabled = true
         isShrinkResources = true
         proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
       }
     }
     bundle {
       language  { enableSplit = true }
       density   { enableSplit = true }
       abi       { enableSplit = true }
     }
     packaging { resources { excludes += setOf("/META-INF/{AL2.0,LGPL2.1}", "DebugProbesKt.bin") } }
   }
   dependencies {
     baselineProfile(project(":baselineprofile"))
     implementation(libs.androidx.profileinstaller)
   }
   ```
   - **R8 full mode** on (`android.enableR8.fullMode=true` in `gradle.properties`)
   - **Baseline Profiles** module — a 20–30% startup win, essentially free, and it directly serves the "does not feel like a wrapper" argument
   - `resConfigs` limited to declared locales
   - `android.enableResourceOptimizations=true`
5. Gradle build performance: `org.gradle.caching=true`, `org.gradle.configuration-cache=true`, `org.gradle.parallel=true`. These matter enormously once builds run thousands of times per month.
6. ktlint + detekt wired into the build and into CI.

**Acceptance criteria:** debug and release both build; release APK < 12 MB; configuration cache reports a hit on a second build.

**Tests:** `TC-S02-AND-001`, `TC-S02-PRF-002`

---

### T-02.2 — Config loading and runtime model (4 h)

**Objective:** Read the embedded config fast enough not to delay the first frame.

**Design:**

- `appconfig.json` ships in `assets/`.
- ⚠️ **Two-phase parse.** Parsing a 40 KB `maximal` config with reflection-based JSON on a cold JIT costs real milliseconds on a budget device.
  - **Phase 1 (main thread, blocking, < 5 ms):** a hand-written streaming reader that extracts only what the first frame needs — theme colours, tab labels/icons, initial URL, splash config.
  - **Phase 2 (background dispatcher):** full parse with `kotlinx.serialization` into the typed model, exposed via a `StateFlow` that the rest of the app collects.
- Use `kotlinx.serialization` with `@Serializable` data classes generated to match the schema (mirror the C#/TS models; a contract test in S09 will assert agreement).
- `ignoreUnknownKeys = true` ⚠️ — a shell built at version N must not crash on a config written at version N+1.

**Acceptance criteria:** phase-1 parse < 5 ms on a mid-range device; full parse off the main thread; unknown keys ignored without error.

**Tests:** `TC-S02-AND-002`, `TC-S02-AND-003`, `TC-S02-PRF-003`

---

### T-02.3 — WebView host with hardening (8 h)

**Objective:** A correctly configured, secure WebView. Most of the value here is in the settings you get right.

**Configuration:**

```kotlin
webView.settings.apply {
    javaScriptEnabled = true
    domStorageEnabled = true
    databaseEnabled = true
    loadWithOverviewMode = true
    useWideViewPort = true
    builtInZoomControls = config.interface.zoomEnabled
    displayZoomControls = false
    mediaPlaybackRequiresUserGesture = false     // for autoplay-muted video
    mixedContentMode = MIXED_CONTENT_NEVER_ALLOW // ⚠️ security
    userAgentString = buildUserAgent(config)     // append, never replace
    cacheMode = WebSettings.LOAD_DEFAULT
    allowFileAccess = false                      // ⚠️ security
    allowContentAccess = false                   // ⚠️ security
    setGeolocationEnabled(config.permissions.location != null)
}
CookieManager.getInstance().setAcceptThirdPartyCookies(webView, true)
```

**⚠️ Security requirements — these are the ones that get platforms breached:**

- `allowFileAccess = false` and `allowUniversalAccessFromFileURLs = false`. A file-scheme WebView with JS enabled can read the app's private storage.
- `MIXED_CONTENT_NEVER_ALLOW`.
- **User agent: append a token, never replace the whole string.** Replacing it breaks feature detection on the customer's site and is a top support ticket in this category.
- Origin gating for the (future) bridge is designed in now: a single `OriginAllowlist` class consulted before any privileged surface is exposed.

**Other work:**

- `WebViewClient` with `shouldOverrideUrlLoading` delegating to the link router (T-02.5).
- `WebChromeClient` for title updates, JS alerts, file chooser (`onShowFileChooser` — essential for form uploads), and permission requests.
- **Warm the WebView during splash** on a background thread so first paint is not delayed by WebView initialisation. This is the single biggest startup win available.
- Custom user-agent, custom headers, and CSS/JS injection hooks driven by `webOverrides`.
- ⚠️ Handle process death and `WebView` state restoration (`saveState`/`restoreState`).
- `WebViewCompat` feature detection with a graceful degradation path when the device's System WebView is below the minimum supported version.

**Acceptance criteria:** loads all three fixture sites; file input opens the chooser and uploads; cookies persist across app restart; a `file://` URL is refused.

**Tests:** `TC-S02-AND-004` … `TC-S02-AND-012`, `TC-S02-SEC-001`, `TC-S02-SEC-002`

---

### T-02.4 — Native chrome (7 h)

**Objective:** Native navigation that config drives. This is the visible difference between a wrapper and an app, and it is the primary Guideline 4.2 mitigation.

**Components:**

1. **Bottom tab bar** (Compose `NavigationBar`) — items, labels, icons, and colours from config; selection state derived from the current URL via each item's `activePattern`; tapping a selected tab scrolls the WebView to top (a small detail users notice).
2. **Top app bar** — title from config or the document `<title>`; action buttons (share, refresh, custom) from config; back affordance when the WebView history is non-empty.
3. **Pull-to-refresh** (`SwipeRefreshLayout`) tinted to the theme, ⚠️ disabled when the WebView is not scrolled to the top or the page owns the gesture.
4. **Splash screen** using the Android 12+ `SplashScreen` API with a `windowSplashScreenAnimationDuration`, exiting only when the shell skeleton is drawn.
5. **Skeleton first paint** — draw the tab bar and app bar with theme colours **before** the WebView has content. This is the `RT-06` differentiator from the master spec and it is what makes the app feel instant.
6. **Back handling** — system back and the predictive-back gesture: WebView history first, then modal dismissal, then tab-root, then exit.

**Acceptance criteria:** all chrome renders from `maximal.json` without code changes; skeleton visible before web content; predictive back behaves correctly on Android 14+.

**Tests:** `TC-S02-AND-013` … `TC-S02-AND-020`

---

### T-02.5 — Link routing engine (5 h)

**Objective:** Decide, for every navigation, where it goes. Correctness here determines whether the app feels coherent.

**Design:**

```kotlin
sealed interface LinkAction {
    data object Internal : LinkAction              // this WebView
    data object NewWindow : LinkAction             // modal WebView
    data object ExternalBrowser : LinkAction       // Custom Tabs
    data object ReaderModal : LinkAction
    data object Block : LinkAction
    data class Deeplink(val target: String) : LinkAction
}

class LinkRouter(rules: List<CompiledRule>) {
    fun resolve(url: String, isRedirect: Boolean, isUserInitiated: Boolean): LinkAction
}
```

**Implementation requirements:**

- ⚠️ **Compile every regex once at startup, with a matching timeout.** A user-supplied catastrophic pattern must not hang the UI thread. S01 rejects the worst at config time; the shell defends anyway.
- Evaluate rules **in declared order**, first match wins. Document this; it is what makes the studio's drag-to-reorder meaningful.
- Fall through to `ExternalBrowser` when nothing matches, and log it — the studio warns about a missing catch-all (`CFG_LINK_RULE_NO_CATCHALL`).
- Use **Chrome Custom Tabs** for external links, not an `ACTION_VIEW` intent. Custom Tabs keep the user in the app's colour scheme and preserve the session — a meaningfully better experience.
- Handle `mailto:`, `tel:`, `sms:`, `intent://`, and file downloads before regex evaluation.
- Cache resolutions in a small LRU keyed by URL — SPA navigation can fire this hundreds of times per session.

**Acceptance criteria:** all rules in `maximal.json` route correctly; `mailto:` opens the mail app; a 200-rule config resolves in < 1 ms per navigation.

**Tests:** `TC-S02-AND-021` … `TC-S02-AND-028`, `TC-S02-PRF-004`

---

### T-02.6 — Connectivity, offline page, error handling (4 h)

**Steps:**

1. `ConnectivityManager.NetworkCallback` producing a `StateFlow<NetworkState>`.
2. Offline page: a **bundled local HTML asset** (never a remote page — that defeats the purpose), themed from config, with a retry button.
3. On `onReceivedError` for the main frame: show the offline page, keep the failed URL, retry when connectivity returns.
4. ⚠️ **Distinguish error types.** A 404 from the customer's site should render the site's own 404, not your offline page. Only network-level failures trigger the offline page. Getting this wrong is a common and embarrassing bug in this category.
5. Show a themed, non-blocking indeterminate progress indicator during main-frame loads.
6. Crash guard: `WebViewClient.onRenderProcessGone` — ⚠️ the WebView renderer can die independently; if unhandled the whole app crashes. Recreate the WebView and reload.

**Acceptance criteria:** airplane mode shows the offline page; reconnect auto-retries; a site 404 shows the site's page; killing the renderer process recovers without a crash.

**Tests:** `TC-S02-AND-029` … `TC-S02-AND-034`

---

### T-02.7 — Test suite (4 h)

**Steps:**

1. **Unit** (JUnit 5 + MockK): link router, config parser, origin allowlist, user-agent builder.
2. **Espresso / Compose UI**: tab switching, app bar title updates, pull-to-refresh, offline page, back handling. Run against the fixture sites so tests do not depend on the internet at large.
3. **Macrobenchmark** module measuring `startupTimeMs` for cold, warm, and hot starts, with the results recorded to a time series so regressions are visible over sprints.
4. Wire all three into GitHub Actions on the public shell repo (free, unmetered), running the UI tests on an emulator via `reactivecircus/android-emulator-runner`.

**Acceptance criteria:** all suites green in CI; startup benchmark asserts the < 300 ms budget and fails the build when exceeded.

**Tests:** `TC-S02-PRF-001`

---

## 5. Test cases (selected detail)

| ID               | Type           | Precondition                | Steps                                      | Expected                                             |
| ---------------- | -------------- | --------------------------- | ------------------------------------------ | ---------------------------------------------------- |
| `TC-S02-AND-004` | Espresso       | `simple` fixture config     | Launch app                                 | Initial URL loads; page title appears in the app bar |
| `TC-S02-AND-009` | Espresso       | `auth` fixture site         | Log in, kill app, relaunch                 | Session persists — still logged in                   |
| `TC-S02-AND-012` | Espresso       | SPA fixture                 | Tap the file input, choose an image        | Chooser opens; file uploads successfully             |
| `TC-S02-AND-016` | Compose UI     | 3-tab config                | Tap tab 2, then tab 2 again                | Navigates; second tap scrolls to top                 |
| `TC-S02-AND-023` | Unit           | Router with `maximal` rules | `resolve("https://external.com/x")`        | `ExternalBrowser`                                    |
| `TC-S02-AND-026` | Unit           | Router with `^(a+)+$` rule  | `resolve` a long non-matching string       | Returns within the timeout; no hang                  |
| `TC-S02-AND-030` | Espresso       | App loaded                  | Enable airplane mode, navigate             | Offline page shown with retry button                 |
| `TC-S02-AND-031` | Espresso       | Offline page shown          | Disable airplane mode                      | Auto-retries and restores the page                   |
| `TC-S02-AND-032` | Espresso       | App loaded                  | Request a URL returning 404 from the site  | The site's own 404 renders, not the offline page     |
| `TC-S02-AND-034` | Instrumented   | App loaded                  | Kill the WebView renderer process          | App recovers and reloads; no crash                   |
| `TC-S02-SEC-001` | Instrumented   | App loaded                  | Navigate to `file:///data/data/<pkg>/`     | Load refused                                         |
| `TC-S02-SEC-002` | Instrumented   | App loaded                  | Load a page with an `http://` sub-resource | Blocked by mixed-content policy                      |
| `TC-S02-PRF-001` | Macrobenchmark | Release build, device       | 10 cold starts                             | Median first frame < 300 ms                          |
| `TC-S02-PRF-002` | CI             | Release build               | Measure arm64 APK                          | < 12 MB                                              |
| `TC-S02-PRF-004` | Unit benchmark | 200-rule router             | 10,000 resolutions                         | < 1 ms mean                                          |

---

## 6. Risks

| Risk                                                                         | Likelihood | Mitigation                                                                                                            |
| ---------------------------------------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------- |
| Startup budget missed                                                        | Medium     | Baseline Profiles + two-phase config parse + eager WebView warm-up are all in scope this sprint precisely for this    |
| WebView behavioural differences across OEMs                                  | **High**   | Test on at least one Samsung and one budget device, not just Pixel emulators. Record a device matrix in `/docs/qa/`.  |
| Scope creep into plugins                                                     | **High**   | ⚠️ No plugins this sprint. No bridge this sprint. Tab bar, app bar, routing, offline. That is the whole scope.        |
| Play Console signup friction (identity verification, developer verification) | Medium     | ⚠️ Start the Play Console registration on **day 1** of this sprint — verification can take days and S03 depends on it |

---

## 7. Deliverables

- `shells/android` public repo with a working, config-driven Kotlin shell
- Release APK < 12 MB meeting the startup budget
- Unit + Espresso + Macrobenchmark suites green in CI
- `docs/qa/android-device-matrix.md`
- Google Play Console account registered and verified
- `SPRINT-02_REVIEW.md`
