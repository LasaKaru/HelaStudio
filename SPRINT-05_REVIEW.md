# Sprint 05 review — codegen engine (iOS)

**Goal:** extend the generator to produce a complete Xcode project, reusing the
architecture, determinism guarantees and golden harness from Sprint 04.

Carried in from Sprint 04: **T-04.3, the asset pipeline**, delivered in full.

## Exit criteria

| Criterion                                                        | Status                                                     |
| ---------------------------------------------------------------- | ---------------------------------------------------------- |
| `generate(config) → Xcode project` for any fixture               | ✅ 28 files, from any valid config                         |
| Double generation is byte-identical                              | ✅ same guarantee as Android, asserted per fixture         |
| Golden snapshots for all fixtures; CI enforces                   | ✅ 14 snapshots, 7 fixtures × 2 platforms                  |
| iOS asset catalogue: app icon, colour sets, launch background    | ✅ single-image app icon, both appearances on every colour |
| `Info.plist` emits usage strings, URL schemes, ATS, orientations | ✅ derived from config, both directions                    |
| Associated domains and privacy manifest                          | ✅ sorted `applinks:`, `PrivacyInfo.xcprivacy` always      |
| Codegen of `maximal.json` for iOS < 3 s                          | ✅                                                         |
| **Generated project builds with `xcodebuild`**                   | ❌ **unverified — there is no Mac**                        |
| Nightly real iOS build on the fixture corpus                     | ⏳ job written; free only once the shell repo is public    |

⚠️ **The central exit criterion is unmet, and it is the important one.** Nothing
in this environment runs `xcodebuild`, `xcodegen` or `plutil`. The suite asserts
that the spec is well-formed YAML, that every plist parses, and that the right
keys appear for a given config — real checks, but not the same as "Xcode accepts
this".

Sprint 04 learned exactly this on Android: the `namespace` bug passed 71 unit
tests and every golden file, because a snapshot records what the generator
produced and not whether the toolchain accepts it. On iOS the gap is wider,
because the toolchain is further away. **iOS generation should be treated as
unproven against a real toolchain**, however green the suite is, until the
Codemagic `ios-verify` workflow runs.

## What shipped

### The pipeline was extracted before the second generator existed

Not after. Two copies of the render loop would have agreed for about a sprint,
then diverged on the next determinism fix — the same mistake the one-directory
rule exists to prevent. `ProjectGenerator` holds every rule that keeps output
byte-identical; a platform supplies only its escaping table, its extra template
values, and the files it generates rather than renders.

The Android generator went from 468 lines to 261 and **its golden files did not
move a byte**. That is the evidence the extraction changed nothing, and it is
the argument for doing it before rather than after.

### `project.yml`, not `project.pbxproj`

`project.pbxproj` keys its objects by 96-bit UUIDs and has no stable public
format. Templating it produces a file nobody can review, which makes the golden
corpus worthless on the platform where mistakes cost most. The generator emits a
60-line YAML spec and XcodeGen builds the project on the Mac.
[ADR 0008](docs/adr/0008-xcode-project-generation.md).

### The asset pipeline, carried over

One uploaded icon becomes ten Android launcher files and one 1024px iOS app
icon. Assets are content-addressed and **verified** — content that does not hash
to its own address is refused rather than embedded in a signed binary.

⚠️ **SkiaSharp, not ImageSharp, and the reason is licensing.** ImageSharp's Six
Labors Split License grants Apache-2.0 terms only under 1M USD annual gross
revenue, and version 4 enforces it with a build-time key — a Release build fails
without one. Found the hard way: Debug was clean and every test passed. Adopting
it would have committed a commercial product to a future payment and a future
build blocker. [ADR 0007](docs/adr/0007-image-pipeline.md), and raised in
`ACTION_REQUIRED.md` because it is a decision made on the user's behalf.

## Measured

| Budget                          | Limit | Measured                    |
| ------------------------------- | ----- | --------------------------- |
| Codegen of `maximal.json`, iOS  | 3 s   | asserted                    |
| Generated Android release APK   | 12 MB | **848 kB**, with real icons |
| Generated project size, Android | —     | 46 files (was 55)           |
| Generated project size, iOS     | —     | 28 files                    |
| Codegen tests                   | —     | **168 green**               |
| Programme total                 | —     | **739 green**               |

IPA size is still unmeasured: it needs a Mac.

## Five bugs, and what caught each

**1. ImageSharp fails Release builds without a licence key** — caught by
_building in Release_. Debug was clean and all 128 tests passed. A `dotnet test`
loop would never have found it.

**2. The adaptive-icon XML still pointed at the placeholder** — caught by
_building a generated project_. Customers would have shipped the placeholder
mark on every Android 8 and later device while the correct icons sat unused
beside it.

**3. `ACCESS_FINE_LOCATION` without `ACCESS_COARSE_LOCATION`** — caught by
_lint on a generated project_. Since Android 12 that request fails at the
permission dialog, so location would never have worked for any customer who
enabled it. Not a lint nicety; a feature that silently does nothing.

**4. Every generated project shipped the shell's own test suite** — caught by
_listing the files in a generated project_, not by reading its golden file. Nine
Kotlin files and seven Swift, reading a fixtures directory that does not exist
there. On iOS `Package.swift` still declared the target, so `swift build` would
have failed on a missing directory before compiling a line.

**5. `build.sh` arrived at 0644** — same inspection. A source-export promise the
customer has to `chmod` first is not much of a promise.

| Mechanism            | Catches                                   |
| -------------------- | ----------------------------------------- |
| Unit tests           | Escaping, sorting, cache-key behaviour    |
| Golden snapshots     | Any change to generated bytes, reviewably |
| Release build        | Licence gates, analyser rules             |
| Real toolchain build | Whether the output compiles               |
| Reading the output   | What nothing thought to assert            |

⚠️ Not one of the five came from reading a diff. The fifth row is the new lesson
of this sprint: a golden file shows what _changed_, and says nothing about what
was wrong from the first run. Bugs 4 and 5 were in every snapshot ever approved.

## Decisions worth recording

- **A template with no registered escaping rule is a hard error.** Adding
  `Package.swift` was refused outright until `.swift` joined the table — the
  guard working rather than an unescaped app name going through. Swift's
  interpolation is `\(…)`, so an unescaped backslash before a parenthesis turns
  an app name into an expression the compiler evaluates.
- **`includeTests` is one flag, not a fork.** The shell keeps its tests; a
  generated project does not. That is the only place the two legitimately
  differ, and keeping it to a flag is what lets the shell go on being the
  template.
- **Prettier does not own generated files.** A routine `pnpm format` would have
  made a committed shell file disagree with its own template and failed the
  anti-drift test. The generator is the authority on its own output.
- **Usage strings cut both ways.** Missing one crashes the app on a device the
  instant a web form asks; an unjustified one is flagged by Apple's static
  analysis and rejected. Both directions are why they are derived per config
  rather than left in the template.

## Not in scope, deliberately

No plugins, no bridge — the seams are there and Sprint 10 fills them. No signed
build: signing is a placeholder because a generated project is cached, exported
and handed to the customer, so a baked-in team id would leak one customer's
identity into another's build.

## Carried into Sprint 06

- **Verify iOS generation against a real toolchain.** The Codemagic
  `ios-verify` workflow, or the nightly macOS job once `shells/ios` is public.
  Until then, treat the iOS generator as unproven.
- Splash logo rendering (`branding.splash.logo`), which needs the Android 12
  splash geometry as well as an iOS launch image
- Tab bar icon generation at 1×/2×/3× as template images
- WCAG contrast warnings on themed colour pairs, which belong in the studio at
  config time rather than in a build
- A coverage gate on the codegen package
