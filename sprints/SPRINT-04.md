# Sprint 04 — Codegen Engine (Android)

|                   |                           |
| ----------------- | ------------------------- |
| **Weeks**         | 9–10                      |
| **Phase**         | 1 — Pipeline              |
| **Capacity**      | 55 h (38 h new work)      |
| **Depends on**    | S01, S02, S03 gate passed |
| **Blocks**        | S05, S07                  |
| **Planned spend** | $0                        |

---

## 1. Sprint goal

Turn `appconfig.json` into a complete, buildable Gradle project — deterministically, reproducibly, and with golden-file tests that make template changes visible.

**The design constraint that governs everything here:** the generated project must be _byte-identical_ for identical inputs. Without that, build caching is impossible and the unit economics in the master spec do not hold.

---

## 2. Exit criteria

- [ ] `generate(config) → project directory` for Android, from any fixture config
- [ ] Generated project builds with `./gradlew assembleDebug` with no manual edits
- [ ] Generating the same config twice produces byte-identical output (proven by test)
- [ ] Golden snapshots committed for all ~20 fixture configs; CI fails on unapproved diffs
- [ ] Icon pipeline: one 1024×1024 source → every density, adaptive icon layers, monochrome
- [ ] Unicode configs (RTL, emoji, CJK) generate valid, correctly-escaped projects
- [ ] Codegen of `maximal.json` completes in < 3 s
- [ ] Coverage ≥ 90% line / 85% branch on the codegen package

---

## 3. Task breakdown

| ID     | Task                                     | Est.     | Priority |
| ------ | ---------------------------------------- | -------- | -------- |
| T-04.1 | Codegen architecture and template engine | 6 h      | P0       |
| T-04.2 | Android project templating               | 9 h      | P0       |
| T-04.3 | Asset pipeline (icons, splash, colours)  | 8 h      | P0       |
| T-04.4 | Determinism and normalisation            | 5 h      | P0       |
| T-04.5 | Golden-file test infrastructure          | 6 h      | P0       |
| T-04.6 | Nightly real-build verification          | 4 h      | P0       |
|        | **Total**                                | **38 h** |          |

---

## 4. Task detail

### T-04.1 — Codegen architecture (6 h)

**ADR `docs/adr/0004-codegen-architecture.md` — decisions:**

| Decision          | Choice                                                       | Rationale                                                                                                                                                  |
| ----------------- | ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Approach          | **Template repo + file transforms**, not AST manipulation    | Templates are readable and diffable; an engineer can open the shell repo and see the real app. AST rewriting of Gradle/Kotlin is fragile and unreviewable. |
| Template engine   | **Scriban** (C#)                                             | Fast, sandboxed, no arbitrary code execution in templates                                                                                                  |
| Template source   | The shell repos, tagged by semver                            | The template _is_ the hand-written shell from S02/S03 with placeholders. One codebase, not two.                                                            |
| Placeholder style | `{{ }}` in `.tmpl` files; non-template files copied verbatim | Keeps most of the shell as plain, compilable Kotlin                                                                                                        |
| Config injection  | Serialise the resolved config into `assets/appconfig.json`   | The shell already reads this at runtime (S02)                                                                                                              |

**Pipeline:**

```
ResolvedConfig
   ↓  materialise template @ shellVersion (from local cache, else clone)
   ↓  render *.tmpl files
   ↓  copy static files
   ↓  generate assets (icons, splash, colours)
   ↓  write resolved appconfig.json
   ↓  write generation manifest (inputs, versions, hashes)
   ↓  normalise (line endings, permissions, ordering)
   → project directory + manifest
```

**Key interfaces:**

```csharp
public interface IProjectGenerator {
    Task<GenerationResult> GenerateAsync(
        ResolvedConfig config,
        ToolchainDescriptor toolchain,
        IFileSink sink,
        CancellationToken ct);
}
```

`IFileSink` abstracts the destination — local directory in tests, a tar stream to R2 in production. Do not write directly to disk from the generator; it makes it untestable and forces a temp directory in every unit test.

**⚠️ The generation manifest is not optional.** Every generated project contains `.shellwright/manifest.json` recording config hash, shell version, toolchain versions, plugin versions, and generator version. Without it, "why does this customer's app behave differently?" is unanswerable.

**Acceptance criteria:** ADR merged; generator interface implemented with an in-memory sink; a trivial template renders.

**Tests:** `TC-S04-GEN-001`, `TC-S04-GEN-002`

---

### T-04.2 — Android project templating (9 h)

Convert `shells/android` into a template by parameterising these files:

| File                             | What is templated                                                                                                                                                            |
| -------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `settings.gradle.kts`            | project name                                                                                                                                                                 |
| `app/build.gradle.kts.tmpl`      | applicationId, versionName, versionCode, minSdk/targetSdk from toolchain, resConfigs from locales, signing config placeholder, **plugin dependency block** (empty until S10) |
| `gradle/libs.versions.toml.tmpl` | exact pinned versions from the toolchain descriptor                                                                                                                          |
| `AndroidManifest.xml.tmpl`       | app label, permissions, **intent filters for deep links**, orientation, `usesCleartextTraffic=false`, queries element for Custom Tabs                                        |
| `res/values/strings.xml.tmpl`    | app name and localised strings (⚠️ XML-escaped — see determinism task)                                                                                                       |
| `res/values/colors.xml.tmpl`     | theme colours                                                                                                                                                                |
| `res/values/themes.xml.tmpl`     | splash theme, status bar style                                                                                                                                               |
| `res/values-night/*.tmpl`        | dark variants                                                                                                                                                                |
| `res/values-<locale>/*.tmpl`     | one directory per configured locale                                                                                                                                          |
| `assets/appconfig.json`          | the resolved config, canonical form                                                                                                                                          |
| `proguard-rules.pro.tmpl`        | base rules + per-plugin rules (S10)                                                                                                                                          |
| `.shellwright/manifest.json`     | generation manifest                                                                                                                                                          |

**⚠️ Escaping is where codegen bugs live.** Each target format needs its own escaper, and each needs a test with the `unicode.json` fixture:

- **Android XML:** `&`, `<`, `>`, `'`, `"`, and ⚠️ apostrophes in strings must be `\'` or the resource compiler fails — a classic, silent, hard-to-debug break
- **Gradle Kotlin DSL:** `$` must be escaped in string literals or it becomes template interpolation
- **JSON:** standard, but ensure NFC normalisation matches S01's canonicaliser
- **Bundle id / file paths:** validated in S01, but re-assert here — never interpolate unvalidated input into a shell command or path

**Deep link generation:** from `config.deepLinks`, emit `<intent-filter android:autoVerify="true">` blocks for each App Links host, plus a custom-scheme filter. ⚠️ Emit them **sorted by host** so output is deterministic.

**Acceptance criteria:** `minimal.json` and `maximal.json` both produce projects that compile; `unicode.json` produces valid XML with correctly escaped apostrophes and RTL text.

**Tests:** `TC-S04-GEN-003` … `TC-S04-GEN-018`

---

### T-04.3 — Asset pipeline (8 h)

**Objective:** One uploaded icon becomes every asset Android needs, deterministically.

**Icons:**
| Output | Sizes | Notes |
|---|---|---|
| `mipmap-<density>/ic_launcher.png` | 48/72/96/144/192 | Legacy |
| `mipmap-<density>/ic_launcher_foreground.png` | 108dp equivalents | ⚠️ Adaptive icon: content must sit in the central 66dp safe zone or it gets clipped on round-icon launchers |
| `mipmap-<density>/ic_launcher_background.png` or a colour | | From config |
| `drawable/ic_launcher_monochrome.xml` or PNG | | Android 13+ themed icons |
| `mipmap-anydpi-v26/ic_launcher.xml` | | Adaptive icon definition |
| Play Store icon | 512×512 | For S15 |

**Implementation:**

- Use **ImageSharp** (C#, cross-platform, no native deps — important on the arm64 Oracle host).
- ⚠️ **Resampling must be deterministic.** Pin the library version, pin the resampler (`Lanczos3`), and strip all metadata (EXIF, timestamps, colour profiles) before writing. PNG encoders that embed a timestamp will destroy your byte-identical guarantee — set the encoder to omit ancillary chunks.
- **Cache generated assets by source-image hash + output spec** in R2. The same icon uploaded by 50 customers is resized once. This is a real cost saving at scale and costs nothing to implement now.
- Validate input at upload time, not generation time: ≥ 1024×1024, square, ⚠️ **no alpha channel for the iOS variant** (Apple rejects it) — warn if alpha is present and offer to flatten against a chosen background.

**Splash:** generate the Android 12+ `SplashScreen` theme attributes plus a fallback windowBackground drawable; render the logo at the correct icon-inset size (⚠️ Android 12 splash icons are clipped to a circle with specific inset rules — get this wrong and every generated app has a cropped logo).

**Colours:** emit `colors.xml` and `values-night/colors.xml`; derive on-colour contrast pairs automatically and ⚠️ warn if any text/background pair falls below WCAG AA 4.5:1 — a small accessibility win that competitors do not offer.

**Acceptance criteria:** one source icon produces all outputs; regenerating produces identical bytes; a 512×512 source is rejected with a clear diagnostic; low-contrast theme colours produce a warning.

**Tests:** `TC-S04-GEN-019` … `TC-S04-GEN-028`

---

### T-04.4 — Determinism and normalisation (5 h)

⚠️ **This task is the difference between a build cache that works and one that never hits.**

**Sources of nondeterminism to eliminate:**

| Source                         | Fix                                                                                                                        |
| ------------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| Dictionary/map iteration order | Sort all collections before emitting. Use `SortedDictionary` or explicit `OrderBy` everywhere.                             |
| Timestamps in generated files  | Never emit a generation timestamp into a hashed file. Put it in the manifest only, and exclude the manifest from the hash. |
| Absolute paths                 | Emit relative paths only                                                                                                   |
| GUIDs / random ids             | Derive deterministically from the config hash where an id is needed                                                        |
| Line endings                   | Force LF everywhere in the normaliser                                                                                      |
| File permissions               | Set explicitly (0644 files, 0755 for `gradlew`)                                                                            |
| PNG encoder metadata           | Strip ancillary chunks; pin encoder version                                                                                |
| Locale-dependent formatting    | ⚠️ `InvariantGlobalization=true` (already set in S00) plus explicit `CultureInfo.InvariantCulture` on every `ToString`     |
| JSON property order            | Use the S01 canonicaliser                                                                                                  |

**The proving test (`TC-S04-GEN-029`):** generate `maximal.json` twice into two in-memory sinks and assert every file's bytes are equal and the file lists are identical. Run it on every PR. Also run it **on a different OS in CI** (Linux and macOS) to catch path-separator and line-ending divergence.

**Acceptance criteria:** double-generation is byte-identical on both Linux and macOS runners.

**Tests:** `TC-S04-GEN-029`, `TC-S04-GEN-030`

---

### T-04.5 — Golden-file test infrastructure (6 h)

**Implementation:**

1. For each fixture config, generate the project and write a **manifest of the tree**: relative path, mode, size, and BLAKE3 hash for every file.
2. Commit the full text content of _text_ files (Gradle, XML, JSON, ProGuard) as approved snapshots; commit only the hash for binaries (PNGs).
3. Use **Verify** with a custom comparer that produces a readable directory diff.
4. CI fails on any unapproved change, printing a per-file diff.
5. `dotnet run --project tools/ApproveGolden` regenerates and approves — but ⚠️ **the PR must show the diff**, and reviewing it is a required checklist item. This is the mechanism that stops you silently breaking 500 customers' apps with a template edit.

**Corpus discipline (from `03_TEST_STRATEGY.md` §4):** every codegen bug fixed from now on adds a fixture. The corpus grows to match reality.

**Acceptance criteria:** golden tests exist for all fixtures; deliberately changing a template fails CI with a readable diff; approval workflow documented.

**Tests:** `TC-S04-GEN-031`, `TC-S04-GEN-032`

---

### T-04.6 — Nightly real-build verification (4 h)

Golden files prove the generator is stable. They do not prove the output _compiles_. That needs a real build.

**Steps:**

1. Extend `nightly.yml`: for 5 representative fixtures (`minimal`, `maximal`, `unicode`, `edge-many-tabs`, `edge-many-linkrules`), generate the project and run `./gradlew assembleDebug lint`.
2. Install each APK on an emulator and run a two-assertion smoke test: the app launches, and the initial URL loads.
3. Record APK size per fixture to a time series; fail if any exceeds the 12 MB budget.
4. Publish artifacts on failure so a broken build can be debugged without reproducing locally.

**Acceptance criteria:** nightly runs green; deliberately breaking a template fails the nightly with a usable log.

**Tests:** `TC-S04-BLD-001` … `TC-S04-BLD-003`

---

## 5. Test cases (selected detail)

| ID               | Type      | Precondition                       | Steps                       | Expected                                                                     |
| ---------------- | --------- | ---------------------------------- | --------------------------- | ---------------------------------------------------------------------------- |
| `TC-S04-GEN-003` | Golden    | `minimal.json`                     | Generate                    | Output matches approved snapshot exactly                                     |
| `TC-S04-GEN-010` | Unit      | `unicode.json`                     | Generate `strings.xml`      | Apostrophes escaped as `\'`; RTL preserved; valid XML per parser             |
| `TC-S04-GEN-013` | Unit      | Config with 3 App Links hosts      | Generate manifest           | Three `autoVerify` intent-filters, sorted by host                            |
| `TC-S04-GEN-021` | Unit      | 1024×1024 source icon              | Generate                    | All densities present; adaptive foreground content within the 66dp safe zone |
| `TC-S04-GEN-024` | Unit      | Icon with alpha channel            | Validate for iOS target     | Warning `CFG_ICON_ALPHA` raised with a flatten suggestion                    |
| `TC-S04-GEN-027` | Unit      | Theme with #FFFFFF text on #F0F0F0 | Generate                    | Contrast warning emitted                                                     |
| `TC-S04-GEN-029` | Property  | Any fixture                        | Generate twice, compare     | Byte-identical, both OSes                                                    |
| `TC-S04-GEN-032` | CI        | Template modified without approval | Run CI                      | Fails with a per-file diff                                                   |
| `TC-S04-BLD-001` | Nightly   | `maximal.json` generated           | `./gradlew assembleDebug`   | Build succeeds, zero lint errors                                             |
| `TC-S04-BLD-002` | Nightly   | APK from `maximal`                 | Install on emulator, launch | Launches; initial URL loads                                                  |
| `TC-S04-PRF-001` | Benchmark | `maximal.json`                     | Generate to memory sink     | < 3 s                                                                        |

---

## 6. Risks

| Risk                                       | Likelihood | Mitigation                                                                                                                                                           |
| ------------------------------------------ | ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Template drift from the hand-written shell | **High**   | ⚠️ There is only _one_ Android codebase. The shell repo _is_ the template. Never fork it. CI builds the shell standalone (with `minimal.json`) as well as generated. |
| Nondeterminism discovered late             | Medium     | T-04.4's double-generation test runs on every PR from the moment the generator exists                                                                                |
| Escaping bugs reach production             | Medium     | The `unicode.json` fixture is mandatory in every golden test; add a new escaping case for every bug found                                                            |
| Golden snapshots become noise nobody reads | **High**   | Keep the corpus small (~20). Make diff review a required PR checklist item. If diffs are routinely large, the templates are wrong.                                   |

---

## 7. Deliverables

- `services/codegen` — Android generator with `IFileSink` abstraction
- `shells/android` parameterised as a template, still standalone-buildable
- Asset pipeline with content-addressed caching
- Golden snapshots for ~20 fixtures + approval tooling
- Nightly real-build verification with size tracking
- `docs/adr/0004-codegen-architecture.md`
- `SPRINT-04_REVIEW.md`
