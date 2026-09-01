# Sprint 03 review — iOS shell MVP

**Goal:** a Swift shell at parity with the Android one, and the same
`appconfig.json` producing an app on both stores.

**This is the M1 kill gate.** If Sprint 03 overruns by more than four weeks, the
master plan says the mobile build treadmill is heavier than assumed and the
programme should be re-scoped rather than continued at the same shape.

## Exit criteria

| Criterion                                                         | Status                                            |
| ----------------------------------------------------------------- | ------------------------------------------------- |
| Swift shell reads the same `appconfig.json` as the Android one    | ✅ same fixture corpus, no iOS-specific config    |
| Config-driven tab bar, top bar, pull-to-refresh                   | ✅ `ShellApp`, unverified on a device             |
| Link rules route identically to Android                           | ✅ **shared corpus, 21 cases, both shells green** |
| Offline page on connectivity loss                                 | ✅ bundled, themed at load                        |
| WebView hardening at parity                                       | ✅ origin allowlist, no cleartext, no file scheme |
| `ShellCore` builds and tests without a Mac                        | ✅ **48 tests, ~1 s, on Linux CI**                |
| IPA installs from TestFlight on a physical iPhone                 | ⏳ needs Apple Developer enrolment and a device   |
| Play internal testing build installs on a physical Android device | ⏳ needs Play Console verification and a device   |
| Both stores' review passed for one manual app                     | ⏳ blocked on the two above                       |

Everything that can be verified without an Apple account, a Mac, or a phone is
verified. The four outstanding criteria are the kill gate itself, and all four
are blocked on the same two things: enrolment and hardware. Both are in
[`ACTION_REQUIRED.md`](ACTION_REQUIRED.md), with the enrolment lead times called
out, because they are the long pole and nothing in the repository can shorten
them.

⚠️ The kill gate cannot be assessed until those criteria are met. What can be
said now is that no _engineering_ obstacle to them was found.

## What shipped

### The split that makes iOS affordable

The shell is two targets, and the boundary is drawn by what they import:
`ShellCore` sees only Foundation, `ShellApp` sees UIKit and WebKit. Every
decision the shell makes — which config keys matter, where a link goes, whether
an origin is allowed, whether a pattern is safe to run — lives in `ShellCore`,
compiles on Linux, and is tested on free CI minutes in about a second.

This is recorded as [ADR 0005](docs/adr/0005-shellcore-shellapp-split.md). Its
consequence is the sprint's real result: iOS logic is now developed at the same
speed as everything else, and the metered macOS minutes are spent only on "does
it build, sign, install, and look right".

### Sign-in, which is the one thing that would have failed review

`AuthenticationRouter` sends OAuth to `ASWebAuthenticationSession` rather than
the web view. This is not a refinement. Google **blocks** `WKWebView` for sign-in
outright, so a shell without it cannot log users into anything that uses Google
as an identity provider — which, for the customers this product targets, is most
of them. It has no Android equivalent, because Custom Tabs already share the
browser session.

### The routing contract

`tests/fixtures/routing/link-routing.json` — 21 cases over 7 rule sets, read by
both shells. The two routers share no code; the behaviour was ported, not the
source, and this corpus is the only thing that catches them drifting.

It earned its place immediately: see below.

## Measured

| Budget                           | Limit     | Measured                          |
| -------------------------------- | --------- | --------------------------------- |
| Phase-one config read (maximal)  | 5 ms      | asserted, `FastConfigReaderTests` |
| Link resolution, 200 rules       | 1 ms mean | asserted, `LinkRouterTests`       |
| `ShellCore` tests                | —         | **48 green**                      |
| Android tests, after this sprint | —         | **123 green** (was 68)            |
| TypeScript / C# validator tests  | —         | **224 / 164 green**               |

IPA size is not yet measured: it needs a Mac. The budget is in
`codemagic.yaml` so the first real build asserts it rather than
reporting it.

## The bug the shared corpus caught

The corpus's hostile case — `^(a+)+$` against sixty `a`s and a `!` — **hung the
Swift test suite indefinitely.** Android passed the same case in under a
millisecond.

The cause was a wrong assumption written into `SafeRegex`. Android bounds a
runaway match by counting the reads `java.util.regex` makes against the
`CharSequence` it is handed; the iOS port tried to do the equivalent with a
deadline checked inside the block passed to `enumerateMatches`. That block is
called for _matches_, not for progress. `NSRegularExpression` is ICU-backed and
does not yield while it backtracks, so the deadline was never evaluated even
once. The defence had a mechanism, a doc comment explaining the mechanism, and no
effect whatsoever.

It would have shipped. Nothing in the iOS suite tested it, and the code looked
right.

The fix is to refuse the pattern before it is ever run, which on iOS is the only
moment intervention is possible. `BacktrackingCheck` is a port of the studio's
`checkRegex`, and rather than trust three ports to stay in step it was made a
contract like the routing one:

`tests/fixtures/regex-safety/patterns.json` — 30 patterns, read by **four**
implementations: TypeScript (studio), C# (API), Kotlin and Swift (shells). All
four agree.

Two engine differences are excluded from the corpus and documented in its README
rather than papered over: JavaScript reads `\p` as a literal `p` outside Unicode
mode where the others reject it, and ICU rejects the empty pattern where the
others accept it as a match-everything. Neither can reach a shell — the schema's
`UrlPattern` sets `minLength: 1` — but a corpus that quietly averaged over them
would be lying about what is actually shared.

Android gained the same structural check, so both shells now refuse a dangerous
pattern rather than one refusing and one merely surviving it. Its character
budget stays behind it as a second layer.

### The residual gap, stated plainly

One difference remains, and it is a difference in outcome rather than mechanism:
a pattern that is _slow but not exponential_ is cut short on Android and is not
on iOS. `^a*a*a*a*a*a*a*a*a*b$` is one — nine sequential stars, no group for a
structural scan to see, polynomial rather than exponential — and it takes 921 ms
against a 28-character input. Android's budget stops it. iOS has nothing that
can.

It degrades rather than hangs, which is why this is a gap and not a defect. It is
recorded in [`docs/qa/shell-parity.md`](docs/qa/shell-parity.md) as the one known
parity gap, and it is the reason the corpus has to grow whenever a new shape is
found rather than being treated as finished.

## Decisions worth recording

- **A doc comment is not a test.** The dead deadline had a careful explanation of
  why it worked. The explanation was wrong, and no reviewer would have caught it
  without running the case. Every defence added from here needs a test that
  fails when the defence is removed.
- **`swift test` on Linux is a first-class gate, not a convenience.** It is what
  keeps the import boundary honest. The moment a `UIKit` import lands in
  `ShellCore` the job fails, and that job is the boundary's only enforcement.
- **macOS CI is opt-in.** `.github/workflows/ios.yml` runs `ShellCore` on Linux
  on every pull request and the Mac job only on `workflow_dispatch`. GitHub bills
  macOS at ten times the Linux rate; running it on every push would spend the
  monthly allowance on a shell whose logic was already tested. The signed build
  comes from Codemagic.
- **Contract corpora are now the default answer to "two implementations".** The
  count is three: validators, routing, regex safety. Sprint 09's bridge will be
  the fourth and will have three implementations rather than two.

## Not in scope, deliberately

No plugins, no bridge, no code generation — the same boundary Sprint 02 held.
No iPad layout, no widgets, no push. The kill gate is about whether an app can be
built and shipped at all, and every hour spent on polish before that question is
answered is an hour bet on an unanswered question.

## Carried into Sprint 04

- The four blocked exit criteria, all waiting on enrolment and hardware
- iOS tab controller, so re-tapping a selected tab scrolls to top (the one
  navigation behaviour Android has and iOS does not)
- `SFSafariViewController` theming to match Custom Tabs
- IPA size budget, measurable at the first Codemagic build
- XCTest metric for cold start, the iOS half of Sprint 02's carried-over budget
