# Shellwright — Android shell

**This is the product.** Everything else in the platform exists to generate and
deliver this app. A build pipeline can be rewritten; a slow, ugly, or crashy
shell is what a customer's users actually experience.

Hand-written first, in Sprint 02. Sprint 04 teaches the generator to produce
what was built here — a much safer order than generating something nobody has
ever run.

> Per ADR 0002 this becomes a separate **public** repository, brought into the
> monorepo as a submodule: public repositories get unmetered GitHub Actions
> minutes including macOS, and customers can read the code that runs on their
> users' devices. It lives in-tree until the GitHub account work in
> `docs/ops/provisioning.md` is done.
>
> ⚠️ Nothing secret may ever be committed here.

## How it starts

Startup order is the reverse of the obvious one, and it is the whole reason the
app feels instant:

1. **Phase one** (`FastConfigReader`, main thread, under 5 ms) pulls only what
   the first frame needs — theme colours, tab labels, the start URL — with a
   hand-written scanner. Not a JSON parser, and it must not grow into one.
2. **The native skeleton is drawn** and the splash dismissed. The app is now
   visibly an app.
3. **Phase two** (`ConfigRepository.load`, background) parses the full typed
   model, builds the web view, and starts loading.

Doing step 3 before step 2 is the difference between an app that appears
instantly and one that shows a white rectangle for half a second.

## Layout

| Path       | What it does                                                     |
| ---------- | ---------------------------------------------------------------- |
| `config/`  | The typed config model, the two-phase parse, and localized text  |
| `routing/` | `LinkRouter` and `SafeRegex` — where every navigation goes       |
| `web/`     | WebView construction and hardening, origin allowlist, user agent |
| `net/`     | Connectivity as a flow, distinguishing "connected" from "usable" |
| `ui/`      | The native chrome: top bar, tab bar, theme, icons                |

## The parts most worth reading

- **`web/ShellWebViewFactory`** — most of its value is in the settings switched
  _off_. A file-scheme WebView with JavaScript enabled can read the app's own
  private storage.
- **`web/OriginAllowlist`** — a security boundary, not a convenience. When the
  bridge lands in Sprint 09, a page outside it must have _no bridge object_, not
  a bridge that refuses calls.
- **`routing/SafeRegex`** — user patterns run on the UI thread on every
  navigation. Sprint 01 rejects catastrophic ones at config time; this defends
  anyway, with a character-budget interrupt that costs nothing on normal input.
- **`web/UserAgent`** — appends, never replaces. Replacing the base string
  breaks feature detection on the customer's own site and is a top support
  ticket in this category.

## Budgets

Asserted, not aspirational (`03_TEST_STRATEGY.md` §12):

| Metric                     | Budget      |
| -------------------------- | ----------- |
| Cold start to first frame  | < 300 ms    |
| Interactive                | < 500 ms    |
| Phase-one config read      | < 5 ms      |
| Link resolution, 200 rules | < 1 ms mean |
| Release APK, arm64 split   | < 12 MB     |

## Building

```bash
export ANDROID_HOME=/path/to/android-sdk
./gradlew :app:assembleDebug        # debug APK
./gradlew :app:testDebugUnitTest    # JVM unit tests, no device needed
./gradlew :app:lintDebug            # lint, with warnings as errors
./gradlew :app:assembleRelease      # R8 full mode, resource shrinking
```

The wrapper pins Gradle by version *and* checksum. Use it rather than a system
`gradle`, so that what CI builds is what you built.

The app reads `app/src/main/assets/appconfig.json`. Swapping that file changes
the app with no code change — which is the entire premise, and worth verifying
by hand before trusting any generator.

## The shell's half of the schema contract

`ShellConfigTest` parses every valid fixture in `tests/fixtures/configs/`, the
same corpus the TypeScript and C# validators are held to. That is deliberate: a
config the validator accepts and the shell cannot parse would pass every check
in the pipeline and fail on a customer's phone.

⚠️ `ShellJson` sets `ignoreUnknownKeys`. A shell built at version N must not
crash on a config written at version N+1 — an app in a store cannot be patched
as quickly as a config can be edited.
