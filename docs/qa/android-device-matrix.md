# Android device matrix

⚠️ WebView behaviour differs between manufacturers more than any other part of
this product. A shell that works on a Pixel emulator and nowhere else is the
most likely way Sprint 02 goes wrong, so the matrix is written down rather than
carried in someone's head.

## What to test on, and why

| Class              | Example                          | Why it earns a slot                                                                                     |
| ------------------ | -------------------------------- | ------------------------------------------------------------------------------------------------------- |
| Google reference   | Pixel 6a or newer                | The baseline everything else is compared against                                                        |
| Samsung            | Galaxy A15 or A54                | ⚠️ The largest Android install base by far, and its System WebView update cadence differs from Google's |
| Budget, low memory | Any 3 GB device on Android 11–12 | Where the renderer process actually gets killed, and where the 300 ms startup budget is genuinely hard  |
| Older OS floor     | Anything on Android 7.0 (API 24) | The minimum the schema allows; easy to break without noticing                                           |
| Large screen       | Any tablet or foldable           | Layout and the tab bar at width, plus configuration changes on fold                                     |

## Findings

Record what surprised you. This section is the point of the file — the table
above is just a shopping list.

| Date | Device | OS / WebView | Finding | Resolution |
| ---- | ------ | ------------ | ------- | ---------- |
|      |        |              |         |            |

## Known hazards to check on every device

- **WebView version.** `WebViewCompat.getCurrentWebViewPackage()` — some
  devices ship a System WebView years behind Chrome, and a user who has
  disabled updates can be further behind still.
- **File chooser.** `<input type="file">` on the SPA fixture. Behaviour on
  Samsung's picker differs from AOSP's.
- **Cookie persistence across a cold start.** The auth fixture, killed from
  recents rather than backgrounded.
- **Renderer death.** Force a low-memory condition and confirm the app rebuilds
  the web view instead of crashing.
- **Predictive back.** Android 14+ only, and the gesture behaves differently
  when web history is non-empty.
- **Pull to refresh vs. a page that owns the gesture.** A site with its own
  scroll container is where this conflicts.
- **Text size at 200%.** The tab bar is where labels clip first.
