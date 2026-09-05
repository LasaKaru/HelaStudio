# Physical device smoke checklist

Run before every release, on one real Android phone and one real iPhone. An
emulator does not catch permission dialogs, background eviction, or the way a
real network fails.

Becomes executable from Sprint 02, when the Android shell exists.

## Launch and navigation

- [ ] Cold start feels instant; nothing flashes white before the first frame
- [ ] Every tab loads and shows as selected
- [ ] Drawer opens, closes, and navigates
- [ ] Top bar title tracks the page, including across client-side routes
- [ ] Back gesture goes back in web history, then exits the app
- [ ] Rotate: layout survives, scroll position is kept
- [ ] Background for five minutes, then return: session intact, no reload flash

## Network

- [ ] Airplane mode shows the offline page, not a browser error
- [ ] Reconnecting recovers without a manual reload
- [ ] Pull to refresh works and matches the theme colour
- [ ] A slow connection shows progress rather than a blank screen

## Links

- [ ] Internal links stay in the app
- [ ] External links open the device browser
- [ ] A deep link from a message opens the right screen, app cold and warm

## Device capability

- [ ] File upload from the camera
- [ ] File upload from the gallery
- [ ] Each permission prompt explains itself in the app's own words
- [ ] Denying a permission degrades gracefully; it never dead-ends
- [ ] Biometric prompt appears and can be cancelled
- [ ] Push notification arrives, and tapping it routes correctly

## Accessibility

- [ ] Full VoiceOver traversal reaches every control
- [ ] Full TalkBack traversal reaches every control
- [ ] 200% text size does not clip or overlap
- [ ] Tap targets are at least 44 points

---

## Sprint 02 additions

The Android shell exists now, so these are runnable rather than aspirational.
Each maps to a Sprint 02 test case that cannot be automated without hardware.

- [ ] `TC-S02-AND-004` — the start URL loads and the page title reaches the app bar
- [ ] `TC-S02-AND-009` — sign in on the auth fixture, kill the app from recents,
      relaunch: still signed in
- [ ] `TC-S02-AND-012` — tap the file input on the SPA fixture, choose an image,
      confirm it uploads
- [ ] `TC-S02-AND-016` — tap the selected tab again: scrolls to top, does not reload
- [ ] `TC-S02-AND-030` — airplane mode, then navigate: the offline page appears
- [ ] `TC-S02-AND-031` — leave airplane mode: the failed page retries by itself
- [ ] `TC-S02-AND-032` — request a URL the site 404s: **the site's own 404**
      renders, not our offline page
- [ ] `TC-S02-AND-034` — kill the WebView renderer process: the app rebuilds and
      reloads rather than crashing
- [ ] `TC-S02-SEC-001` — navigate to `file:///data/data/<pkg>/`: refused
- [ ] `TC-S02-SEC-002` — load a page with an `http://` sub-resource: blocked
