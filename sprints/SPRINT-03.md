# Sprint 03 — iOS Shell MVP + Manual Store Proof ⚠️ KILL GATE

|                   |                                       |
| ----------------- | ------------------------------------- |
| **Weeks**         | 7–8                                   |
| **Phase**         | 0 — Proof                             |
| **Capacity**      | 55 h (38 h new work)                  |
| **Depends on**    | S01, S02                              |
| **Blocks**        | S05, S09 — and the entire programme   |
| **Planned spend** | $99 (Apple Developer Program, annual) |

---

## 1. Sprint goal

Build the iOS shell to parity with Android, then **get one real app onto TestFlight and Google Play internal testing** — signed, reviewed where applicable, and installed on a physical device from the store, not from a cable.

⚠️ **This is the programme's kill gate.** Everything after Sprint 03 assumes that a config can become a distributed app. If this sprint cannot be completed within four weeks of overrun, the mobile build treadmill is heavier than this plan assumes and the programme should be re-scoped — most likely to an Android-first launch — rather than continued at the same shape.

---

## 2. Exit criteria

- [ ] IPA installs from **TestFlight** on a physical iPhone and loads the `simple` fixture site
- [ ] AAB uploaded to **Google Play internal testing**; installs from the Play link on a physical Android device
- [ ] iOS shell reaches feature parity with Sprint 02: tab bar, nav bar, pull-to-refresh, link routing, offline page
- [ ] Both builds produced by **Codemagic free tier**, not from a local Mac — proving you never need to own Apple hardware to develop
- [ ] ⚠️ Cold start to first frame < 300 ms on iOS, measured
- [ ] IPA < 25 MB
- [ ] The shells behave identically against all three fixture sites (documented parity checklist)
- [ ] A written record of every store friction point encountered — this becomes the publishing wizard's content in S15

---

## 3. Task breakdown

| ID     | Task                                                | Est.     | Priority |
| ------ | --------------------------------------------------- | -------- | -------- |
| T-03.1 | Apple Developer Program enrolment and signing setup | 4 h      | P0       |
| T-03.2 | iOS project skeleton and startup optimisation       | 5 h      | P0       |
| T-03.3 | WKWebView host with hardening                       | 8 h      | P0       |
| T-03.4 | Native chrome parity                                | 7 h      | P0       |
| T-03.5 | Link routing + offline parity                       | 4 h      | P0       |
| T-03.6 | Codemagic CI: signed builds for both platforms      | 6 h      | P0       |
| T-03.7 | Store submission: TestFlight + Play internal        | 4 h      | P0       |
|        | **Total**                                           | **38 h** |          |

---

## 4. Task detail

### T-03.1 — Apple Developer Program enrolment and signing (4 h)

⚠️ **Start this on day 1.** Apple enrolment can take 24–48 hours, occasionally longer if identity verification is escalated. Everything else in the sprint is blocked behind it.

**Steps:**

1. Enrol in the Apple Developer Program ($99/yr). Individual is fine for now; ⚠️ note that publishing under an organisation later requires a D-U-N-S number and a separate enrolment — record this in the publishing knowledge base, because your customers will hit it.
2. Create an **App Store Connect API key** (Team Key, `App Manager` role). Download the `.p8` — ⚠️ it is downloadable exactly once. Store it in the password manager, never in the repo.
3. Register a bundle identifier and create an app record in App Store Connect.
4. **Understand `fastlane match` before using it.** `match` stores certificates and profiles in an encrypted git repo. This is the mechanism you will later productise for customer signing (S14), so learn it properly now rather than clicking through Xcode's automatic signing.
   ```
   fastlane match init          # private git repo for certs
   fastlane match appstore      # creates/fetches distribution cert + profile
   ```
5. Document every step, every error, and every wait in `docs/publishing/apple-enrolment.md`. **This document is a product asset**, not a chore — it becomes the wizard content and the knowledge base in S15/S16.

**Acceptance criteria:** an App Store Connect API key authenticates successfully from a script; `match` produces a distribution certificate and profile; the process is fully documented.

**Tests:** `TC-S03-PUB-001`, `TC-S03-PUB-002`

---

### T-03.2 — iOS project skeleton and startup optimisation (5 h)

**Steps:**

1. Create `shells/ios` — **public repo**, for unmetered GitHub Actions macOS minutes on unsigned verification builds.
2. Xcode project (not a workspace yet — CocoaPods arrives with plugins in S10). Swift 6, iOS 15 minimum, SwiftUI for native surfaces, UIKit for the WebView host.
3. **Startup optimisation, configured now:**
   - ⚠️ **Nothing expensive in `application(_:didFinishLaunchingWithOptions:)`.** Every millisecond here is a millisecond of blank screen.
   - Two-phase config parse mirroring Android: a minimal synchronous read for first-frame needs, full `Codable` decode on a background queue.
   - Warm the `WKWebView` and its `WKProcessPool` during the launch screen.
   - `os_signpost` instrumentation around launch phases so the benchmark can attribute regressions.
   - Build settings: `SWIFT_COMPILATION_MODE = wholemodule` for release, `DEAD_CODE_STRIPPING = YES`, `STRIP_INSTALLED_PRODUCT = YES`, `ENABLE_TESTABILITY = NO` in release.
4. Launch screen as a storyboard rendering the themed skeleton — ⚠️ iOS requires a storyboard or `UILaunchScreen` dictionary; a static image will not adapt to the config's colours, so use `UILaunchScreen` with a colour name the generator can rewrite.
5. SwiftLint + swift-format in the build and in CI.

**Acceptance criteria:** app launches to a themed skeleton; release build produces an archive; `os_signpost` traces visible in Instruments.

**Tests:** `TC-S03-IOS-001`, `TC-S03-PRF-002`

---

### T-03.3 — WKWebView host with hardening (8 h)

**Configuration:**

```swift
let config = WKWebViewConfiguration()
config.websiteDataStore = .default()                  // persistent cookies
config.allowsInlineMediaPlayback = true
config.mediaTypesRequiringUserActionForPlayback = []
config.defaultWebpagePreferences.allowsContentJavaScript = true
config.processPool = sharedProcessPool                // share across windows
config.applicationNameForUserAgent = userAgentSuffix  // ⚠️ append, don't replace

let webView = WKWebView(frame: .zero, configuration: config)
webView.allowsBackForwardNavigationGestures = true    // native edge-swipe
webView.scrollView.contentInsetAdjustmentBehavior = .never
```

**⚠️ iOS-specific traps to handle in this task — these are the ones that generate support tickets:**

| Trap                                                                                              | Handling                                                                                                                                                      |
| ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Cookies do not persist reliably** across launches with the default store in some configurations | Use `.default()` data store; explicitly sync `HTTPCookieStorage` on background; test with the `auth` fixture                                                  |
| **OAuth redirects break** in WKWebView (IdPs detect embedded browsers and refuse)                 | Route auth URLs to `ASWebAuthenticationSession`. This is `RT-08` in the master spec and it is the single highest-value iOS fix in the whole shell.            |
| **Safe areas** — content under the notch / Dynamic Island / home indicator                        | Constrain the WebView to the safe area; expose `env(safe-area-inset-*)` correctly by _not_ fighting the viewport                                              |
| **Keyboard covers inputs**                                                                        | Handle `keyboardWillShow` and adjust `scrollView.contentInset`; emit a keyboard event for the (future) bridge                                                 |
| **Rubber-band scrolling** past content looks wrong with a native tab bar                          | `scrollView.bounces` configurable; match background colour to the theme so overscroll shows brand colour, not white                                           |
| **Pull-to-refresh conflicts** with the page's own scroll                                          | Attach `UIRefreshControl` to `webView.scrollView`, disabled unless `contentOffset.y <= 0`                                                                     |
| **File uploads**                                                                                  | `WKUIDelegate` handles it automatically since iOS 14, but camera and photo-library permission strings must exist in `Info.plist` or the app crashes on tap ⚠️ |
| ⚠️ **ATS blocks cleartext**                                                                       | Do not add `NSAllowsArbitraryLoads`. S01 rejects `http://` at config time; keep ATS strict. Adding the exception is an App Review flag.                       |

**Other work:**

- `WKNavigationDelegate` → link router.
- `WKUIDelegate` → JS alerts, new-window requests, file chooser.
- Custom headers via `URLRequest` (⚠️ note: WKWebView drops custom headers on cross-origin redirects — document this limitation; it is a real and frequently misunderstood constraint).
- CSS/JS injection via `WKUserScript` at `.atDocumentStart` and `.atDocumentEnd`.
- Handle `webViewWebContentProcessDidTerminate` — the iOS equivalent of Android's renderer death; recreate and reload.

**Acceptance criteria:** all three fixture sites load; login on the `auth` site persists across relaunch; OAuth mock flow completes via `ASWebAuthenticationSession`; file upload works.

**Tests:** `TC-S03-IOS-002` … `TC-S03-IOS-012`, `TC-S03-SEC-001`

---

### T-03.4 — Native chrome parity (7 h)

Mirror Sprint 02's chrome using `UITabBarController` + `UINavigationController` hosting the WebView controller, with SwiftUI for any generated native surfaces later.

**Parity checklist (must match Android exactly in behaviour, not necessarily in pixels):**

- Bottom tab bar from config; active tab derived from URL pattern; re-tap scrolls to top
- Nav bar: dynamic title, share / refresh / custom actions, back affordance from WebView history
- Pull-to-refresh, theme-tinted
- Themed skeleton painted before web content
- Back: WebView history → modal dismiss → tab root. On iOS also support the interactive edge-swipe (`allowsBackForwardNavigationGestures`) ⚠️ which must not conflict with the nav controller's own pop gesture — resolve by disabling the nav controller's gesture while the WebView can go back.
- Dark mode: follow system / force light / force dark, driven by config, applied to both native chrome and an injected CSS class

**Write the parity checklist into `docs/qa/shell-parity.md` as a table with an Android column and an iOS column.** It becomes a permanent release gate.

**Acceptance criteria:** every row of the parity checklist passes on both platforms with the same config.

**Tests:** `TC-S03-IOS-013` … `TC-S03-IOS-020`

---

### T-03.5 — Link routing + offline parity (4 h)

- Port the `LinkRouter` logic from Kotlin to Swift. ⚠️ **Port the behaviour, not the code** — but port the _test fixtures_ verbatim. The same JSON fixture corpus must drive both routers and produce identical decisions. This is the first instance of the cross-platform contract-testing pattern that S09 formalises for the bridge.
- `SFSafariViewController` for external links (the iOS equivalent of Custom Tabs; keeps session and theme).
- `NWPathMonitor` for connectivity.
- Bundled offline HTML, themed, with retry.
- Distinguish network failures from site 4xx/5xx exactly as on Android.

**Acceptance criteria:** the shared router fixture suite passes identically on both platforms; offline behaviour matches.

**Tests:** `TC-S03-IOS-021` … `TC-S03-IOS-026`

---

### T-03.6 — Codemagic CI: signed builds for both platforms (6 h)

**Objective:** Prove that a full signed build pipeline runs on free infrastructure with no Mac of your own. This de-risks the largest cost assumption in the business plan.

**Steps:**

1. Connect Codemagic to the shell repos.
2. Write `codemagic.yaml`:
   ```yaml
   workflows:
     ios-release:
       instance_type: mac_mini_m2
       max_build_duration: 60
       environment:
         groups: [appstore_credentials] # encrypted: API key id, issuer id, .p8
         xcode: latest
         cocoapods: default
       cache:
         cache_paths:
           - ~/Library/Caches/CocoaPods
           - ~/Library/Developer/Xcode/DerivedData
           - $HOME/Library/Caches/org.swift.swiftpm
       scripts:
         - name: Fetch signing
           script: app-store-connect fetch-signing-files "$BUNDLE_ID" --type IOS_APP_STORE --create
         - name: Build
           script: xcode-project build-ipa --project ... --scheme Shell
       artifacts: [build/ios/ipa/*.ipa, /tmp/xcodebuild_logs/*.log]
       publishing:
         app_store_connect:
           auth: integration
           submit_to_testflight: true
   ```
3. ⚠️ **Cache aggressively even though minutes are free-ish.** DerivedData and SwiftPM caches cut an iOS build roughly in half. You are also measuring the cost-per-build number that S08's economics depend on — measure it with caching on, since that is the production configuration.
4. Add an Android workflow producing a signed AAB, with the upload keystore in an encrypted environment group.
5. **Record the numbers:** wall-clock build time cold vs warm, minutes consumed, and the extrapolated cost per build at $0.095/min. Put them in `COSTS.md`. These numbers directly validate or invalidate master spec §16.

**Acceptance criteria:** a git push produces a signed IPA on TestFlight and a signed AAB artifact, with no manual step; cached build is measurably faster than cold; measurements recorded.

**Tests:** `TC-S03-BLD-001` … `TC-S03-BLD-004`

---

### T-03.7 — Store submission: TestFlight + Play internal (4 h)

**Objective:** Complete a real submission end to end, and write down everything that hurt.

**Steps:**

1. **TestFlight:** upload, complete export-compliance, add yourself as an internal tester, install on a physical iPhone from the TestFlight app.
   - ⚠️ Note: internal TestFlight does not require App Review, but **external** TestFlight does. Do an external group submission too if time allows — it is the cheapest possible probe of how App Review will treat a shell app, and that intelligence is worth more than the time it costs.
2. **Google Play:** create the app, complete the Data Safety form, content rating questionnaire, target-audience declaration, and privacy policy URL; upload the AAB to internal testing; install from the opt-in link on a physical device.
   - ⚠️ Also complete **Android developer verification** registration while you are here — enforcement began 30 September 2026 in the first wave of markets and expands globally in 2027. Registering your package name and signing keys now avoids a scramble later, and the walkthrough becomes product content for S15.
3. **Write `docs/publishing/friction-log.md`.** Every form, every wait, every rejection, every confusing field, with screenshots. Estimate how long each step took a person who already knows what they are doing.

This friction log is the single most commercially valuable artifact of Phase 0. It is the raw material for the publishing wizard (S15), the readiness score (S16), and the knowledge base that is one of the five differentiation pillars.

**Acceptance criteria:** app installed from TestFlight and from Play internal testing on physical devices; friction log written; developer verification registration submitted.

**Tests:** `TC-S03-PUB-003` … `TC-S03-PUB-006`

---

## 5. Test cases (selected detail)

| ID               | Type                   | Precondition               | Steps                                                 | Expected                                                                    |
| ---------------- | ---------------------- | -------------------------- | ----------------------------------------------------- | --------------------------------------------------------------------------- |
| `TC-S03-IOS-005` | XCUITest               | `auth` fixture             | Log in, force-quit, relaunch                          | Still logged in                                                             |
| `TC-S03-IOS-006` | XCUITest               | `auth` fixture             | Trigger the mock OAuth redirect                       | Opens `ASWebAuthenticationSession`; completes; returns to app authenticated |
| `TC-S03-IOS-009` | XCUITest               | Any config                 | Tap a file input, pick a photo                        | Picker opens; upload succeeds; no crash from a missing usage string         |
| `TC-S03-IOS-011` | Manual                 | Device with Dynamic Island | Load a full-bleed page                                | Content respects safe areas; no clipping                                    |
| `TC-S03-IOS-018` | XCUITest               | 3-tab config               | Swipe from the left edge with WebView history present | Goes back in web history, not out of the tab                                |
| `TC-S03-IOS-021` | Unit (shared fixtures) | Router fixture corpus      | Run all routing fixtures on Swift router              | Decisions identical to the Kotlin router for all cases                      |
| `TC-S03-SEC-001` | Manual                 | Release build              | Inspect `Info.plist`                                  | No `NSAllowsArbitraryLoads`; all usage strings present                      |
| `TC-S03-BLD-002` | Automated              | Codemagic configured       | Push a commit                                         | Signed IPA produced and delivered to TestFlight with no manual step         |
| `TC-S03-BLD-003` | Measurement            | Two consecutive builds     | Compare durations                                     | Warm build materially faster; both durations recorded in `COSTS.md`         |
| `TC-S03-PUB-004` | Manual                 | TestFlight build processed | Install on a physical iPhone                          | Installs and launches; loads the fixture site                               |
| `TC-S03-PUB-005` | Manual                 | AAB uploaded               | Install from the Play internal-testing link           | Installs and launches                                                       |
| `TC-S03-PRF-002` | XCTest metric          | Release build on device    | 10 cold launches                                      | Median first frame < 300 ms                                                 |

---

## 6. Risks

| Risk                                                                | Likelihood | Impact                 | Mitigation                                                                                                                                                                                                                                                                                          |
| ------------------------------------------------------------------- | ---------- | ---------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ Apple enrolment delayed                                          | Medium     | Blocks the sprint      | Start day 1. If it slips past day 5, front-load T-03.2–T-03.5 and move signing to the end.                                                                                                                                                                                                          |
| ⚠️ External TestFlight review rejects the shell under Guideline 4.2 | **Medium** | **Programme-defining** | This is exactly the intelligence the sprint exists to gather. If rejected, the feedback tells you precisely how much native surface is required — which directly sizes the `RT-14` native-surfaces work. **A rejection here is a successful sprint, not a failed one**, provided you learn from it. |
| Codemagic free minutes exhausted mid-sprint                         | Medium     | Delays                 | 500 min ≈ 33 ten-minute builds. Cache well, do not push on every commit, and use the free public-repo GitHub Actions macOS runner for unsigned verification builds.                                                                                                                                 |
| iOS/Android behavioural divergence discovered late                  | **High**   | Rework                 | The shared router fixture corpus and the parity checklist exist to surface this now                                                                                                                                                                                                                 |
| Play developer verification friction                                | Medium     | Delays Android         | Begin registration on day 1 alongside Apple enrolment                                                                                                                                                                                                                                               |

---

## 7. Deliverables

- `shells/ios` public repo at parity with Android
- App live on **TestFlight** and **Google Play internal testing**, installed on physical devices
- `codemagic.yaml` producing signed builds for both platforms with caching
- `docs/qa/shell-parity.md` — the permanent parity gate
- `docs/publishing/apple-enrolment.md` and `docs/publishing/friction-log.md`
- Measured build times and cost-per-build recorded in `COSTS.md`
- `SPRINT-03_REVIEW.md` — **including an explicit go / no-go / re-scope decision against the kill gate**

---

## 8. ⚠️ Kill-gate decision record

At the end of this sprint, write the following in `SPRINT-03_REVIEW.md` and answer honestly:

1. Did a config-driven app reach a physical device from both stores? **Yes / No**
2. Total elapsed time from Sprint 00 start: **\_\_\_ weeks** (plan: 8)
3. Actual-to-estimate ratio across S00–S03: **\_\_\_** (re-baseline the whole programme if > 1.4)
4. Total spend: **$\_\_\_** (plan: $124)
5. Did external App Review accept the app? **Yes / No / Not attempted** — if no, what did the reviewer ask for?
6. **Decision: Continue / Re-scope to Android-first / Stop.**

Answer 6 before starting Sprint 04.
