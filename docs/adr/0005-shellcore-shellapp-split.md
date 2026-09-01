# 5. Splitting the iOS shell into ShellCore and ShellApp

Date: 2026-09-01

## Status

Accepted

## Context

Every line of iOS code in this programme has a problem that the Android code
does not: it can only be compiled on Apple hardware. There is no Mac in the
development loop, macOS minutes on GitHub are billed at ten times the Linux rate
against a fixed monthly allowance, and Codemagic's free tier is 500 minutes a
month — enough for roughly sixty full builds, which is a week of ordinary work,
not a phase.

The naive consequence is that iOS development stalls. Every change waits on a
remote build, feedback arrives in eight-minute increments, and the cost of
finding a typo is the same as the cost of shipping a release.

But most iOS bugs are not iOS bugs. Deciding where a link opens, parsing a
config, checking an origin against an allowlist, refusing a pattern that would
backtrack exponentially, composing a user-agent string — none of that needs
UIKit, WebKit, a simulator, or a Mac. It is ordinary logic that happens to be
written in Swift.

## Decision

The iOS shell is two SwiftPM targets, split by what they import.

| Target      | Imports                                 | Where it builds      | What is in it                                                                                            |
| ----------- | --------------------------------------- | -------------------- | -------------------------------------------------------------------------------------------------------- |
| `ShellCore` | Foundation only                         | Linux, macOS, iOS    | Config reading, link routing, origin allowlist, backtracking check, offline page, user-agent composition |
| `ShellApp`  | UIKit, WebKit, `AuthenticationServices` | Apple platforms only | View controllers, the web view and its delegates, the app delegate, the authentication router            |

`ShellCore` is a library product with its own test target. `swift test` runs it
on Linux, in about a second, on free CI minutes, on every pull request. It is
also where both shared fixture corpora are asserted: `tests/fixtures/routing/`
and `tests/fixtures/regex-safety/`, the same files the Kotlin shell and the two
validators read.

`ShellApp` depends on `ShellCore` and is compiled on a Mac, by hand or by
Codemagic. It contains no decisions — only the wiring that turns a `LinkAction`
into a pushed view controller.

## Consequences

The iOS logic is developed and tested at the same speed as everything else, and
the metered minutes are spent on the part that genuinely needs them: does it
build, does it sign, does it install, does it look right.

The rule that makes this work is a rule about imports, and it is easy to break
by accident. One `import UIKit` in `ShellCore` — for a `UIColor`, say — and the
whole target stops building on Linux, the fast tests stop running, and nobody
notices until the next remote build. Two things guard it:

- `ShellCore` has no dependency on `ShellApp` and cannot gain one; SwiftPM
  refuses the cycle.
- The Linux `core` job in `.github/workflows/ios.yml` fails immediately when a
  platform import appears. That job is not an optimisation. It is the boundary's
  only enforcement.

The split also costs something real. A type needed on both sides has to live in
`ShellCore` and be adapted in `ShellApp` — `OfflinePage` renders HTML as a
string rather than returning a configured `WKWebView`, and the shell's colours
travel as hex rather than as `UIColor`. That indirection is the price of the
boundary and should be paid without complaint; the moment it is argued away for
convenience, the fast loop goes with it.

`ShellCore` is deliberately the same shape as the Kotlin shell's pure layer, so
that behaviour ported between them stays reviewable side by side. Where the two
must agree, they agree by reading the same fixture files rather than by having
been written carefully.
