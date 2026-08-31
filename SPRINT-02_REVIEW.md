# Sprint 02 review — Android shell MVP

**Goal:** hand-write a Kotlin shell that reads an embedded `appconfig.json` and
renders native chrome around a web view, meeting the startup and size budgets.

**This is the product.** Everything else in the platform exists to generate and
deliver it.

## Exit criteria

| Criterion                                                                | Status                                       |
| ------------------------------------------------------------------------ | -------------------------------------------- |
| Config-driven tab bar, top bar with dynamic title, pull-to-refresh       | ✅                                           |
| Link rules route correctly: internal in-WebView, external to Custom Tabs | ✅ 68 unit tests over the routing table      |
| Offline page on connectivity loss, recovery on reconnect                 | ✅ bundled asset, themed at load, auto-retry |
| Swapping `appconfig.json` changes the app with no code change            | ✅ the whole shell reads from the asset      |
| Release APK (arm64 split) under 12 MB                                    | ✅ **0.80 MB**                               |
| Espresso smoke suite green in CI                                         | ⏳ needs an emulator                         |
| Cold start under 300 ms, interactive under 500 ms, by Macrobenchmark     | ⏳ needs a device                            |
| APK installs on a physical Android device                                | ⏳ needs a device                            |

Everything that can be verified without hardware is verified. The three
outstanding criteria all need a real device or an emulator; each keeps its test
case ID and is listed in `.github/workflows/android.yml` and
`docs/qa/physical-device-smoke.md` so it is picked up rather than forgotten.

⚠️ Google Play Console registration should be started now, not in Sprint 03.
Developer verification can take days and the Sprint 03 kill gate depends on it.

## What shipped

The startup order is the sprint's real content, and it is the reverse of the
obvious one:

1. `FastConfigReader` reads only what the first frame needs — colours, tab
   labels, the start URL — with a hand-written scanner, on the main thread, in
   well under the 5 ms budget.
2. The native skeleton is drawn and the splash dismissed. The app is now
   visibly an app.
3. The full config is parsed on a background dispatcher, the web view is built
   and hardened, and loading begins.

Doing step 3 before step 2 is the difference between an app that appears
instantly and one that shows a white rectangle for half a second. It is also the
primary mitigation against an App Store guideline 4.2 rejection: the reviewer
sees native chrome before they see a web page.

## Measured

| Budget                                  | Limit     | Measured                     |
| --------------------------------------- | --------- | ---------------------------- |
| Release APK                             | 12 MB     | **0.80 MB**                  |
| Phase-one config read (maximal fixture) | 5 ms      | asserted in `TC-S02-PRF-003` |
| Link resolution, 200 rules              | 1 ms mean | asserted in `TC-S02-PRF-004` |
| Unit tests                              | —         | **68 green**                 |

The APK is an order of magnitude under budget because the shell is genuinely
small: R8 full mode, resource shrinking, and no plugins yet. That headroom is
what the fifteen plugins of Sprint 10 will spend, which is why every plugin has
to publish its size delta.

## Two bugs the tooling caught

**`didCrash()` is API 26; `minSdk` is 24.** Renderer-process recovery — the code
whose entire job is to stop the app crashing — would itself have crashed on
Android 7. Android lint found it. Unit tests could not have: the path only runs
when a renderer dies.

**A `Context` held in a `ViewModel`.** The instance was the application context,
so it would not have leaked, but lint is right that the shape is wrong. The fix
was better than a suppression: the view model now holds resolved strings and an
offline-page renderer rather than a context, which also makes it testable on the
JVM.

## Decisions worth recording

- **The backtracking defence is a character budget, not a timeout.** A watchdog
  thread or a timer costs something on every match; counting reads on the
  `CharSequence` the matcher polls costs nothing on the ordinary case and still
  bounds the pathological one. The shell defends even though Sprint 01 rejects
  catastrophic patterns at config time, because a stored config may predate that
  rule.
- **Only network-level failures show the offline page.** An HTTP status means
  the server answered, so the customer's own 404 renders. Replacing a carefully
  designed error page with a generic one is a common complaint about apps in
  this category.
- **The user agent is appended to, never replaced.** Replacing it breaks feature
  detection on the customer's own site, and is a top support ticket in this
  category.
- **The origin allowlist exists before the bridge does.** Sprint 09 gates the
  bridge on it. Building the boundary first means gating is never something
  bolted onto a surface that already exists.

## Not in scope, deliberately

No plugins. No bridge. No code generation. The sprint file calls scope creep
into plugins a high risk, and the shell is the wrong place to discover plugin
combinatorics.

## Carried into Sprint 03

- Espresso, Macrobenchmark, and instrumented security tests, all blocked only on
  emulator time
- Physical device testing across the matrix in `docs/qa/android-device-matrix.md`
- Google Play Console registration
