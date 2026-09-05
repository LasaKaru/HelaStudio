# Shellwright — iOS shell

Parity with the Android shell, and the second half of Phase 0's proof: that a
config can become an app in a store.

> Per ADR 0002 this becomes a separate **public** repository, brought into the
> monorepo as a submodule — public repositories get unmetered GitHub Actions
> macOS minutes, and customers can read the code that runs on their users'
> phones. It lives in-tree until the GitHub account work in
> `docs/ops/provisioning.md` is done.
>
> ⚠️ Nothing secret may ever be committed here.

## The split, and why it matters

The shell is two modules:

| Module      | What it is                                                                                                              | Builds on                     |
| ----------- | ----------------------------------------------------------------------------------------------------------------------- | ----------------------------- |
| `ShellCore` | Config model, two-phase parse, link routing, regex safety, origin allowlist, user agent, offline page. Foundation only. | **Anywhere**, including Linux |
| `ShellApp`  | UIKit and WebKit host.                                                                                                  | Apple platforms only          |

That split is deliberate and load-bearing. The parts most worth testing — the
routing table, the security boundary, the startup parse — are kept free of the
platform, so they compile and their tests run on a machine that is not a Mac.
It mirrors the Android decision to write `OriginAllowlist` against
`java.net.URI` rather than `android.net.Uri`.

```bash
swift build      # ShellCore
swift test       # ShellCore's suite, no Xcode needed
```

`ShellApp` needs Xcode. The project is generated rather than committed:

```bash
brew install xcodegen && xcodegen generate && open Shellwright.xcodeproj
```

A `.pbxproj` is a merge-conflict generator, and Sprint 04 has to _generate_ an
iOS project from a config anyway — so the shell is built the way the generator
will have to build it.

## The parts most worth reading

- **`AuthenticationRouter`** — the highest-value iOS-specific fix in the whole
  shell (`RT-08`). ⚠️ Identity providers detect embedded browsers and refuse to
  authenticate in them; Google blocks `WKWebView` sign-in outright. A shell that
  routes OAuth through its own web view simply cannot log users in, and the
  failure looks like the customer's bug. `ASWebAuthenticationSession` is the
  sanctioned path and shares Safari's cookie jar.
- **`WebViewFactory`** — most of its value is in what is _not_ set.
  `NSAllowsArbitraryLoads` is never added: Sprint 01 rejects `http://` at config
  time, so ATS stays strict, and an ATS exception is a flag during App Review.
- **`ShellNavigationDelegate`** — only network-level failures show the offline
  page. An HTTP status means the server answered, so the customer's own 404
  renders. `webViewWebContentProcessDidTerminate` is iOS's renderer death;
  unhandled, the view goes blank and stays blank.
- **`SafeRegex` and `BacktrackingCheck`** — ⚠️ the one place iOS is genuinely
  weaker than Android, and the place a plausible-looking defence turned out to
  do nothing. Android bounds a runaway match at match time by counting the reads
  `java.util.regex` makes against the `CharSequence` it is handed. There is no
  iOS equivalent: `NSRegularExpression` is ICU-backed and does not yield while
  it backtracks, so a deadline — including one checked from the block passed to
  `enumerateMatches`, which is called for matches and not for progress — is
  never evaluated at all. The first version of this file did exactly that, and
  the shared corpus caught it by hanging.

  The only moment iOS can intervene is before the pattern is ever run, so
  `BacktrackingCheck` refuses dangerous _shapes_ structurally. It is a port of
  the studio's `checkRegex`, held to `tests/fixtures/regex-safety/` along with
  the C# validator and the Kotlin shell. A slow-but-not-exponential pattern that
  the scan cannot see still costs iOS what it costs; see
  `docs/qa/shell-parity.md`.

## The routing contract

`ShellCore`'s router and the Kotlin router share no code. They are held together
by `tests/fixtures/routing/link-routing.json`, which both test suites read and
must agree on for every case.

It is the same technique that keeps the TypeScript and C# validators identical.
`tests/fixtures/regex-safety/` is the third instance and the widest: four
implementations of the backtracking heuristic, in four languages. Sprint 09
formalises it again for the bridge.

⚠️ Adding a routing behaviour means adding its cases to that corpus **first**.

## Building

`ShellCore` needs no Mac. On Linux or macOS, from this directory:

```
swift build
swift test
```

That is the whole fast loop, and it is what runs on every pull request
(`.github/workflows/ios.yml`). `ShellApp` needs Xcode:

```
brew install xcodegen && xcodegen generate
open Shellwright.xcodeproj
```

⚠️ `Shellwright.xcodeproj` is generated from `project.yml` and is **not**
committed. Regenerate it after changing targets, resources, or capabilities.

### Codemagic

`codemagic.yaml` lives in the **repository root**, not here — that is the only
place Codemagic looks for it. It defines two workflows:

| Workflow         | Needs an Apple account | When it runs   |
| ---------------- | ---------------------- | -------------- |
| `ios-verify`     | no                     | by hand        |
| `ios-testflight` | yes                    | push to `main` |

Run `ios-verify` first. It builds unsigned, needs no enrolment, and is the
cheapest proof that the toolchain assumption behind Phase 0 holds.

⚠️ Neither workflow runs on pull requests, deliberately. The free tier is 500
macOS minutes a month and they are the scarcest minutes in the programme; pull
requests are covered by the Linux job instead. See ADR 0005.

## Budgets

| Metric                     | Budget      |
| -------------------------- | ----------- |
| Cold start to first frame  | < 300 ms    |
| Phase-one config read      | < 5 ms      |
| Link resolution, 200 rules | < 1 ms mean |
| IPA                        | < 25 MB     |
