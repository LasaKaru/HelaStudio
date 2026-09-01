# Changelog

All notable changes to this project are recorded here, in the
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. This project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

#### Sprint 00 — Foundations

- Monorepo scaffold: pnpm workspaces, Turborepo, and a .NET 10 solution.
- Standards enforcement: `TreatWarningsAsErrors` and `AnalysisLevel=latest-all`
  for C#; ESLint `strict-type-checked` with `noUncheckedIndexedAccess` and
  `exactOptionalPropertyTypes` for TypeScript; Prettier, `.editorconfig`,
  lefthook pre-commit hooks, and Conventional Commits.
- CI pipeline with path filtering, run cancellation, dependency caching, a
  single aggregated `gate` status check, and a nightly stub.
- Secret scanning tuned for this product: Apple `.p8`/`.p12` shapes, Google
  service-account JSON, and Android keystore passwords.
- Three fixture websites — static, client-routed, and cookie-authenticated with
  a mock OAuth redirect chain — served by a dependency-free local server.
- App Studio scaffold: React 18 and Vite, with a 200 kB gzipped bundle budget
  enforced by `size-limit` (currently 109 kB).
- ADR 0001 (record decisions) and ADR 0002 (monorepo with public shells).

#### Sprint 01 — Configuration schema and validation

- `appconfig.json` v1 as JSON Schema 2020-12, with a user-facing `title` and
  `description` on every property so the studio renders help text for free.
- Validation engine in **both** TypeScript and C#, producing byte-identical
  diagnostics, canonical forms, and cache keys — asserted against a shared
  golden corpus by a cross-language contract test.
- 19 semantic rules covering store-rejection causes that JSON Schema cannot see:
  origin coverage, catastrophic regex backtracking, unjustified permissions,
  plugin conflicts and platform floors, icon alpha channels, and credentials
  pasted into configuration.
- Canonical JSON: key-sorted, NFC-normalised, shortest round-trip numbers,
  nulls omitted. Property-tested for order-independence over 1,000 generated
  cases per invariant.
- Three-way BLAKE3 cache key (`codeKey`, `assetKey`, `contentKey`) so an
  asset-only or content-only change skips the full recompile path.
- Migration framework with a proven, reversible v0-to-v1 path and golden files.
- Fixture corpus of 29 configurations, including one per diagnostic code.
- Generated TypeScript types, with a CI check that regeneration produces no diff.
- ADR 0003 (schema v1) and ADR 0004 (three-way cache key).
- `docs/reference/diagnostics.md`, generated from the code table.

### Performance

Measured against the budgets in `03_TEST_STRATEGY.md` §12, asserted in CI:

| Budget                          | Limit  | Measured |
| ------------------------------- | ------ | -------- |
| Validate the maximal fixture    | 50 ms  | 0.5 ms   |
| Hash the maximal fixture        | 5 ms   | 0.22 ms  |
| Validate 200 link rules         | 50 ms  | 2.8 ms   |
| Studio initial bundle (gzipped) | 200 kB | 109 kB   |

#### Sprint 02 — Android shell

- Config-driven Kotlin shell: two-phase startup parse, hardened WebView, native
  top bar and tab bar, link routing, connectivity and offline handling.
- `FastConfigReader` — a hand-written scanner that reads only what the first
  frame needs, so the native skeleton is drawn before the full config is parsed.
- `SafeRegex` — a character-budget interrupt so a user-supplied pattern cannot
  hang the UI thread on a device, defending even though Sprint 01 rejects the
  worst at config time. Sprint 03 added a structural check in front of it.
- `OriginAllowlist` — the security boundary the JavaScript bridge will be gated
  on in Sprint 09, built before there is anything privileged to gate.
- WebView hardening: file and content access off, universal file access off,
  mixed content never allowed, user agent appended rather than replaced.
- Offline page bundled as an asset and themed at load time, shown only for
  network-level failures so a site's own 404 still renders.
- Renderer-process death recovers by rebuilding the web view instead of taking
  the app down with it.
- 68 JVM unit tests, including the link-routing and first-frame-parse budgets.

#### Sprint 03 — iOS shell

- Config-driven Swift shell at parity with the Android one: two-phase startup
  parse, hardened `WKWebView`, native chrome, link routing, offline handling.
- Split into `ShellCore` (Foundation only, builds and tests on Linux) and
  `ShellApp` (UIKit and WebKit, Apple-only), so the shell's logic is developed
  and tested without a Mac and metered macOS minutes are spent only on building,
  signing and installing. Recorded as ADR 0005.
- `AuthenticationRouter` — OAuth is routed to `ASWebAuthenticationSession`
  rather than the web view. Identity providers refuse to authenticate in an
  embedded browser and Google blocks `WKWebView` sign-in outright, so without
  this a shell cannot log users in at all.
- Shared routing contract, `tests/fixtures/routing/link-routing.json`: 21 cases
  over 7 rule sets, read by both shells, which share no routing code.
- Shared backtracking-heuristic contract,
  `tests/fixtures/regex-safety/patterns.json`: 30 patterns, read by all four
  implementations — the studio, the API, and both shells.
- `BacktrackingCheck` in Swift and Kotlin — a port of the studio's `checkRegex`,
  so both shells refuse a dangerous pattern rather than merely surviving it.
- Codemagic pipeline (`codemagic.yaml`, at the repository root) with an unsigned
  verification workflow that needs no Apple account, and a TestFlight workflow
  that runs only on `main`.
- iOS CI: `ShellCore` built and tested on free Linux minutes on every pull
  request; the macOS job is opt-in.
- 48 Swift unit tests. Programme total 559.

#### Sprint 04 — code generation for Android

- `services/codegen` turns a resolved `appconfig.json` into a complete Gradle
  project: 55 files that build with `./gradlew assembleRelease` into a real
  820 kB APK, with no manual edits.
- The Android shell is its own template. `shells/android/templates` holds the
  five parameterised files; the committed concrete files are their rendering
  against the shell's own config, asserted in CI. One Android codebase, not two
  (ADR 0006).
- Escaping is a property of the template model rather than of each call site, so
  a template author cannot forget it. A template with no registered escaping
  rule is a hard error, not a silent fall back to none.
- `IFileSink` keeps the generator off the filesystem: in memory for tests, a
  directory for real builds, a stream to R2 later. A duplicate output path is an
  error rather than a last-write-wins overwrite.
- Determinism is asserted, not assumed: byte-identical output per fixture, key
  order in the source config proven irrelevant, no timestamps or absolute paths
  in any generated file, explicit permission bits, LF endings, NFC throughout.
- Golden snapshots for 7 fixtures with `tools/ApproveGolden` to regenerate them.
- Nightly job builds every corpus fixture with Gradle and asserts the 12 MB APK
  budget per fixture.
- Two fixtures added for bugs actually found: `edge-hostile-text.json`
  (`@Bob's "Diner" & Grill <$5`) and `edge-portrait-locked.json`.
- 89 codegen tests. Programme total 660.

#### Sprint 05 — code generation for iOS, and the asset pipeline

- `services/codegen` now produces both platforms from one config: 46 files of
  Gradle project and 28 of Xcode project.
- Shared `ProjectGenerator` base, extracted before the second generator existed
  rather than after. The Android generator shrank from 468 lines to 261 and its
  golden files did not move a byte.
- `project.yml` for XcodeGen rather than a templated `project.pbxproj`, which
  keys objects by 96-bit UUIDs and has no stable format (ADR 0008).
- `Info.plist` usage strings derived per config in both directions: a missing
  one crashes the app on a device, an unjustified one is a rejection.
- `PrivacyInfo.xcprivacy` emitted with zero plugins — the shell reads
  `UserDefaults`, which alone makes the manifest mandatory at upload.
- Associated domains, custom URL schemes and iPad orientations, all sorted and
  all derived from the config.
- Asset pipeline (Sprint 04's T-04.3, carried over): one uploaded icon becomes
  ten Android launcher files and a 1024px iOS app icon, deterministically.
- Content-addressed asset store that **verifies** — content not hashing to its
  own address is refused rather than embedded in a signed binary.
- SkiaSharp rather than ImageSharp, for licensing rather than capability
  (ADR 0007).
- Generated projects no longer ship the shell's own test suite, and `build.sh`
  arrives executable.
- 168 codegen tests. Programme total 739.

### Changed

- `vitest` to 3.2.6 and `vite` to 6.4.3, clearing two critical and one high
  advisory.
- `@size-limit/preset-app` replaced with `@size-limit/file`. The preset pulled
  headless Chrome to estimate execution time; the budget that matters is
  transfer size, and dropping it removed three more advisories.

### Fixed

- The C# canonicaliser was not NFC-normalising at all, because
  `InvariantGlobalization=true` makes `String.Normalize` a silent no-op for
  non-ASCII. A decomposed accent would have hashed differently in each language.
- `RenderProcessGoneDetail#didCrash` is API 26 and `minSdk` is 24, so renderer
  recovery would have crashed on Android 7.
- ImageSharp fails Release builds without a licence key, which a `dotnet test`
  loop never reaches: Debug was clean and all 128 tests passed.
- The adaptive-icon XML still pointed at the shell's placeholder, so a generated
  app would have shown the placeholder mark on every Android 8 and later device.
- `ACCESS_FINE_LOCATION` was granted without `ACCESS_COARSE_LOCATION`. Since
  Android 12 that request fails at the permission dialog, so location would
  never have worked for any customer who enabled it.
- Every generated project shipped the shell's own test suite, reading a fixtures
  directory that does not exist there; on iOS `Package.swift` still declared the
  target, so `swift build` would have failed before compiling a line.
- The generated `build.sh` arrived at 0644 and could not be run.
- Codegen read `JsonValue` only in its parsed form, so any config assembled in
  memory — which is what the API will do for every real customer — would have
  thrown. Every fixture comes from a file, so every test passed.
- Generated projects took the Gradle `namespace` from the customer's bundle id,
  putting `R` and `BuildConfig` in a different package from the Kotlin sources
  importing them. Every generated project failed to compile; all 71 unit tests
  and every golden file passed. Found by running a real Gradle build.
- A fixed `screenOrientation` trips two Android lint checks rather than one, and
  lint runs with warnings as errors in generated projects, so every customer
  choosing portrait would have got a failing build.
- The iOS backtracking defence had no effect whatsoever. It enforced a deadline
  from the block passed to `enumerateMatches`, which `NSRegularExpression` calls
  for matches and not for progress; ICU never yields while backtracking, so the
  deadline was never evaluated once. `^(a+)+$` hung the Swift suite indefinitely
  while Android passed the same case in under a millisecond. Patterns are now
  refused structurally, before they can run. Found by the shared routing corpus
  on its first run.
