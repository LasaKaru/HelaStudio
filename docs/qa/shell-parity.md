# Shell parity checklist

The Android and iOS shells must behave identically given the same config. Not
pixel-identical — each platform should look native — but _behaviourally_
identical, because a customer configures once and expects both apps to do the
same thing.

This is a permanent release gate. A row that differs is either a bug or a
deliberate platform difference, and a deliberate one gets a note saying why.

**Legend:** ✅ implemented and tested · ◐ implemented, needs a device to verify ·
⏳ not yet built

## Startup

| Behaviour                                     | Android | iOS | Notes                                        |
| --------------------------------------------- | ------- | --- | -------------------------------------------- |
| Phase-one config read under 5 ms              | ✅      | ✅  | Same hand-written scanner, ported            |
| Native skeleton drawn before web content      | ◐       | ◐   | The 4.2 mitigation; needs a device to time   |
| Splash dismissed on skeleton, not on web load | ◐       | ◐   |                                              |
| Cold start to first frame under 300 ms        | ⏳      | ⏳  | Macrobenchmark / XCTest metric               |
| Unknown config keys ignored, not fatal        | ✅      | ✅  | A shell at version N reading a config at N+1 |

## Navigation

| Behaviour                                             | Android | iOS | Notes                                                   |
| ----------------------------------------------------- | ------- | --- | ------------------------------------------------------- |
| Tab bar from config                                   | ◐       | ◐   |                                                         |
| Active tab from URL pattern                           | ◐       | ◐   |                                                         |
| Re-tapping the selected tab scrolls to top            | ◐       | ⏳  | iOS needs the tab controller in S04                     |
| Top bar title from `<title>`                          | ◐       | ◐   |                                                         |
| Share, refresh, custom actions                        | ◐       | ⏳  |                                                         |
| Back: web history, then tab root, then exit           | ◐       | ◐   |                                                         |
| Edge-swipe back does not fight the native pop gesture | —       | ◐   | iOS only; resolved by yielding while web history exists |

## Link routing

| Behaviour                                      | Android | iOS | Notes                                                  |
| ---------------------------------------------- | ------- | --- | ------------------------------------------------------ |
| First matching rule wins, in declared order    | ✅      | ✅  | Shared corpus                                          |
| Unmatched links go to the browser              | ✅      | ✅  | Shared corpus                                          |
| `mailto:`, `tel:`, `sms:` leave the app        | ✅      | ✅  | Shared corpus                                          |
| `file:`, `javascript:`, `data:` always blocked | ✅      | ✅  | Shared corpus                                          |
| Document extensions go to the download flow    | ✅      | ✅  | Shared corpus                                          |
| An uncompilable pattern is skipped, not fatal  | ✅      | ✅  | Shared corpus                                          |
| An unknown action degrades to the browser      | ✅      | ✅  | Shared corpus                                          |
| A catastrophic pattern cannot hang the UI      | ✅      | ✅  | Both refuse it up front — shared corpus, and see below |
| A merely _slow_ pattern is bounded             | ✅      | ❌  | Android only. The one known parity gap; see below      |
| External links keep the app's colour scheme    | ◐       | ⏳  | Custom Tabs / `SFSafariViewController`                 |

⚠️ The backtracking defence differs by necessity. Android counts reads on the
`CharSequence` the matcher polls; `NSRegularExpression` offers no such hook, so
iOS enforces a deadline from the enumeration block. Different mechanism, same
observable behaviour — which is exactly what the shared corpus checks.

## Web view hardening

| Behaviour                           | Android | iOS | Notes                                   |
| ----------------------------------- | ------- | --- | --------------------------------------- |
| File and content access disabled    | ✅      | ✅  | iOS has no equivalent to enable         |
| Mixed content blocked               | ✅      | ✅  | ATS on iOS, explicit on Android         |
| User agent appended, never replaced | ✅      | ✅  | Structurally enforced on iOS            |
| Cookies persist across cold start   | ◐       | ◐   | The `auth` fixture, killed from recents |
| Origin allowlist denies by default  | ✅      | ✅  | Same case table on both                 |
| Web content process death recovers  | ◐       | ◐   | Needs memory pressure to trigger        |

## Offline

| Behaviour                                    | Android | iOS | Notes                                  |
| -------------------------------------------- | ------- | --- | -------------------------------------- |
| Bundled page, never fetched                  | ✅      | ✅  |                                        |
| Themed from config at load time              | ✅      | ✅  |                                        |
| Only network failures trigger it             | ✅      | ✅  | A site 404 renders the site's own page |
| Auto-retry on reconnect                      | ◐       | ◐   |                                        |
| Retries the failed URL, not the offline page | ✅      | ✅  |                                        |

## Platform differences that are deliberate

| Difference                                                                | Why                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| ------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| iOS routes sign-in to `ASWebAuthenticationSession`                        | ⚠️ Identity providers refuse to authenticate in an embedded browser. Google blocks `WKWebView` sign-in outright, so a shell that does not do this simply cannot log users in. Android's Custom Tabs already share the browser session, so no equivalent is needed.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| A slow-but-not-exponential pattern is cut short on Android and not on iOS | ⚠️ **The one gap where the platforms genuinely differ in outcome, not just mechanism.** Both shells refuse a pattern whose _shape_ can explode (`BacktrackingCheck`, held to `tests/fixtures/regex-safety/` alongside the studio and the API). Behind that, Android also budgets every match by counting the reads `java.util.regex` makes against the `CharSequence` it was handed. iOS has nothing equivalent: `NSRegularExpression` is ICU-backed and does not yield while backtracking, so neither a deadline nor the block passed to `enumerateMatches` can interrupt it. A pattern the structural check misses — `^a*a*a*a*a*a*a*a*a*b$` is one, polynomial rather than exponential, and groupless so there is nothing for the scan to see — costs Android nothing and costs iOS about a second per navigation. It degrades rather than hangs, which is why this is a gap and not a defect, but it is the reason the corpus must grow whenever a new shape is found. |
| iOS cannot replace the base user agent                                    | WebKit composes it and takes only a suffix. Android must achieve the same by discipline.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Android declares permissions in the manifest; iOS in `Info.plist`         | ⚠️ On iOS a _missing_ usage string crashes the app the moment a web form asks. On Android a missing permission is a denial. Both are generated from the same config.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
