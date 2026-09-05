# Sprint 05 — Codegen Engine (iOS)

|                   |                      |
| ----------------- | -------------------- |
| **Weeks**         | 11–12                |
| **Phase**         | 1 — Pipeline         |
| **Capacity**      | 55 h (38 h new work) |
| **Depends on**    | S03, S04             |
| **Blocks**        | S08                  |
| **Planned spend** | $0                   |

---

## 1. Sprint goal

Extend the codegen engine to produce a complete, buildable Xcode project, reusing the architecture, determinism guarantees, and golden-file harness from Sprint 04.

⚠️ **Xcode project files are the hardest generation target in this system.** `project.pbxproj` is an ordered plist with UUID-keyed objects and no stable public format. Do not hand-template it. See T-05.1.

---

## 2. Exit criteria

- [ ] `generate(config) → Xcode project` for any fixture config
- [ ] Generated project builds with `xcodebuild -scheme Shell -sdk iphonesimulator` with no manual edits
- [ ] Double-generation is byte-identical (same guarantee as Android)
- [ ] Golden snapshots for all fixtures; CI enforces
- [ ] iOS asset catalogue generated: app icon set, colour sets, launch screen
- [ ] `Info.plist` correctly emits usage strings, URL schemes, associated domains, ATS policy
- [ ] Nightly real iOS build on all 5 representative fixtures
- [ ] Codegen of `maximal.json` for iOS < 3 s

---

## 3. Task breakdown

| ID     | Task                                                     | Est.     | Priority |
| ------ | -------------------------------------------------------- | -------- | -------- |
| T-05.1 | Xcode project generation strategy (ADR + implementation) | 9 h      | P0       |
| T-05.2 | Info.plist and entitlements generation                   | 7 h      | P0       |
| T-05.3 | Asset catalogue generation                               | 7 h      | P0       |
| T-05.4 | Build settings, schemes, and signing placeholders        | 6 h      | P0       |
| T-05.5 | Golden tests + nightly iOS build                         | 9 h      | P0       |
|        | **Total**                                                | **38 h** |          |

---

## 4. Task detail

### T-05.1 — Xcode project generation strategy (9 h)

**⚠️ The core problem:** `project.pbxproj` uses 96-bit hex UUIDs as object keys. Naive generation produces different UUIDs each run, destroying determinism. Hand-editing it is unmaintainable.

**Options considered (record in `docs/adr/0005-xcode-project-generation.md`):**

| Option                                               | Verdict                                                                                                                        |
| ---------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Template the raw `.pbxproj`                          | ❌ Unmaintainable; breaks whenever Xcode's format shifts                                                                       |
| Generate with **XcodeGen** (`project.yml` → project) | ✅ **Recommended.** Mature, deterministic, YAML input we can template trivially, and it is the tool most iOS teams already use |
| Generate with **Tuist**                              | Viable, heavier, Swift-based — more capability than needed                                                                     |
| Swift Package Manager only                           | ❌ Cannot express app targets, entitlements, and extensions adequately                                                         |

**Recommendation: template `project.yml` and run XcodeGen.** You template a readable 60-line YAML file instead of a 3,000-line plist. ⚠️ Pin the XcodeGen version in the toolchain descriptor and verify determinism — XcodeGen derives UUIDs from paths and names, which is deterministic, but assert it with the double-generation test rather than trusting it.

**Template `project.yml.tmpl`:**

```yaml
name: { { app.name_safe } }
options:
  bundleIdPrefix: { { app.bundle_prefix } }
  deploymentTarget: { iOS: '{{ toolchain.ios_min }}' }
  createIntermediateGroups: true
settings:
  base:
    MARKETING_VERSION: '{{ app.version_name }}'
    CURRENT_PROJECT_VERSION: '{{ app.version_code }}'
    PRODUCT_BUNDLE_IDENTIFIER: { { app.bundle_id } }
    SWIFT_VERSION: '6.0'
    DEAD_CODE_STRIPPING: YES
    SWIFT_COMPILATION_MODE: wholemodule # release
targets:
  Shell:
    type: application
    platform: iOS
    sources: [Sources, Resources]
    info: { path: Shell/Info.plist }
    entitlements: { path: Shell/Shell.entitlements }
    dependencies: [] # plugins injected here in S10
```

**Also in scope:** a fallback path that runs `pod install` when any plugin requires CocoaPods (dormant until S10, but the generator must already emit a `Podfile.tmpl` and know when to invoke it).

**Acceptance criteria:** XcodeGen produces a project that opens in Xcode and builds; double generation is byte-identical.

**Tests:** `TC-S05-GEN-001` … `TC-S05-GEN-006`

---

### T-05.2 — Info.plist and entitlements generation (7 h)

⚠️ **This task prevents the two most common iOS runtime crashes and several App Review rejections.** An iOS app that touches the camera without `NSCameraUsageDescription` does not fail gracefully — it crashes, instantly, on device.

**Generated from config:**

| Key                                              | Source                        | ⚠️ Gotcha                                                                                                                   |
| ------------------------------------------------ | ----------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `CFBundleDisplayName`                            | `app.name`                    | Max 30 chars; must be localised per configured locale                                                                       |
| `CFBundleShortVersionString` / `CFBundleVersion` | version fields                | `CFBundleVersion` must strictly increase per upload or App Store Connect rejects it                                         |
| `UISupportedInterfaceOrientations`               | `interface.orientation`       | Separate iPad key; iPad apps **must** support all orientations unless justified                                             |
| `NSCameraUsageDescription` etc.                  | permissions + enabled plugins | **Must be present if the API is reachable at all**, even if unused at runtime — Apple's static analysis flags it            |
| `NSAppTransportSecurity`                         | —                             | Emit strict; never `NSAllowsArbitraryLoads`                                                                                 |
| `CFBundleURLTypes`                               | `deepLinks.customScheme`      | Sorted for determinism                                                                                                      |
| `UILaunchScreen`                                 | branding                      | Colour name referencing the generated asset catalogue                                                                       |
| `ITSAppUsesNonExemptEncryption`                  | `false` by default            | Omitting it forces a manual question on every TestFlight upload — setting it saves the customer a step every single release |
| `UIBackgroundModes`                              | plugins                       | Only when a plugin needs it; ⚠️ an unjustified background mode is a rejection cause                                         |
| `PrivacyInfo.xcprivacy`                          | plugins + platform APIs       | Required privacy manifest; see below                                                                                        |

**Entitlements** (`Shell.entitlements`): associated domains (`applinks:<host>` per configured Universal Link host, sorted), keychain access groups, push (from S20), app groups (when a plugin needs an extension).

**⚠️ Privacy manifest:** emit `PrivacyInfo.xcprivacy` with `NSPrivacyAccessedAPITypes` reasons for every required-reason API the shell touches — `UserDefaults` (`CA92.1`), file timestamps, disk space, system boot time. The shell uses `UserDefaults`, so this is required _even with zero plugins_. Missing or incorrect manifests are a hard rejection at upload. Build this correctly now; the plugin fragments merge into it in S10.

**Acceptance criteria:** generated plist validates with `plutil -lint`; every permission implied by config has a usage string; privacy manifest present and well-formed; a config requesting camera with no plugin using it produces the `CFG_PERMISSION_UNJUSTIFIED` warning from S01.

**Tests:** `TC-S05-GEN-007` … `TC-S05-GEN-018`

---

### T-05.3 — Asset catalogue generation (7 h)

**Outputs into `Assets.xcassets`:**

- `AppIcon.appiconset` — ⚠️ modern Xcode accepts a **single 1024×1024** icon and generates the rest, which dramatically simplifies this. Emit that form, and a `Contents.json` declaring it. ⚠️ **The source must have no alpha channel** — flatten against the configured background if present.
- `AccentColor.colorset` and one colorset per themed colour, with `any`/`dark` appearance variants
- `LaunchBackground.colorset` referenced by `UILaunchScreen`
- Tab bar icons: generate from the same icon sources as Android, at 1×/2×/3×, as template images so iOS tints them

**Determinism:** `Contents.json` files must have sorted keys and stable formatting — they are JSON, so reuse the S01 canonicaliser. Strip PNG metadata as in Sprint 04.

**Reuse:** the icon-resizing service from T-04.3 is shared; only the output spec differs. ⚠️ Do not fork the image pipeline — parameterise it. A second image pipeline is a second source of visual bugs.

**Acceptance criteria:** catalogue compiles via `actool` without warnings; icon renders correctly on device; dark-mode colours switch correctly.

**Tests:** `TC-S05-GEN-019` … `TC-S05-GEN-026`

---

### T-05.4 — Build settings, schemes, and signing placeholders (6 h)

**Steps:**

1. Emit release/debug configurations with the optimisation settings from S03 (`wholemodule`, dead-code stripping, symbol stripping).
2. Emit a shared scheme so `xcodebuild -scheme Shell` works from a clean checkout — ⚠️ Xcode does not create shared schemes by default and a non-shared scheme is invisible to CI, which is a classic first-CI-run failure.
3. **Signing must be a placeholder, not a value.** Emit `CODE_SIGN_STYLE = Manual` with the team id and profile name supplied by the build environment at build time. ⚠️ Never bake signing identity into the generated project — it is customer-specific and, in S14, secret.
4. Emit `ExportOptions.plist` templates for `development`, `ad-hoc`, and `app-store` export methods.
5. Emit a `build.sh` in the project root that builds the project standalone with documented prerequisites. This is the beginning of the source-export feature (`BD-10`) and it costs almost nothing now.

**Acceptance criteria:** `xcodebuild -scheme Shell -sdk iphonesimulator build` succeeds on a clean checkout; `build.sh` works on a Mac with only Xcode installed.

**Tests:** `TC-S05-GEN-027` … `TC-S05-GEN-030`

---

### T-05.5 — Golden tests + nightly iOS build (9 h)

1. Extend the golden harness to iOS output. Same corpus, same approval workflow.
2. ⚠️ Filter `project.pbxproj` and `.xcworkspace` internals out of _text_ snapshots if XcodeGen's output proves version-sensitive — snapshot `project.yml` (the input we control) plus a hash of the generated project, rather than the plist itself. Record the choice in the ADR.
3. Extend `nightly.yml` with an iOS job on **GitHub Actions macOS runners against the public shell repo** — unsigned simulator builds, unmetered and free. ⚠️ Do not spend Codemagic minutes on nightly verification; reserve those for signed builds.
4. Build all 5 representative fixtures, boot each in a simulator, and run a two-assertion XCUITest smoke (launches, loads initial URL).
5. Record IPA/app size per fixture; fail above the 25 MB budget.

**Acceptance criteria:** nightly iOS job green and free; deliberately breaking a template fails it with a usable log.

**Tests:** `TC-S05-BLD-001` … `TC-S05-BLD-004`

---

## 5. Test cases (selected detail)

| ID               | Type        | Precondition               | Steps                             | Expected                                                                |
| ---------------- | ----------- | -------------------------- | --------------------------------- | ----------------------------------------------------------------------- |
| `TC-S05-GEN-004` | Property    | Any fixture                | Generate twice, compare trees     | Byte-identical                                                          |
| `TC-S05-GEN-009` | Unit        | Config enabling location   | Generate `Info.plist`             | `NSLocationWhenInUseUsageDescription` present and non-empty             |
| `TC-S05-GEN-011` | Unit        | Any config                 | Generate `Info.plist`             | `ITSAppUsesNonExemptEncryption` present                                 |
| `TC-S05-GEN-013` | Unit        | 2 Universal Link hosts     | Generate entitlements             | Two sorted `applinks:` entries                                          |
| `TC-S05-GEN-015` | Unit        | Minimal config, no plugins | Generate                          | `PrivacyInfo.xcprivacy` present with the `UserDefaults` reason declared |
| `TC-S05-GEN-016` | Unit        | Any config                 | `plutil -lint Info.plist`         | Valid                                                                   |
| `TC-S05-GEN-022` | Unit        | Icon with alpha            | Generate `AppIcon.appiconset`     | Alpha flattened against the configured background; no alpha in output   |
| `TC-S05-GEN-028` | Integration | Generated project          | `xcodebuild -list`                | The `Shell` scheme is visible (i.e. shared)                             |
| `TC-S05-BLD-002` | Nightly     | `maximal.json`             | Build for simulator, boot, launch | Launches; initial URL loads                                             |
| `TC-S05-PRF-001` | Benchmark   | `maximal.json`             | Generate                          | < 3 s                                                                   |

---

## 6. Risks

| Risk                                               | Likelihood            | Mitigation                                                                                                                                            |
| -------------------------------------------------- | --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ XcodeGen output not byte-stable across versions | Medium                | Pin the version in the toolchain descriptor; snapshot `project.yml` plus a project hash rather than the plist; assert with the double-generation test |
| Xcode version bump breaks generation               | **Certain, annually** | The toolchain descriptor makes the version explicit and testable. Run N and N−1 in nightly from S08 onward.                                           |
| Missing usage string crashes an app in production  | Medium                | T-05.2's exhaustive permission mapping plus the S01 `CFG_PERMISSION_UNJUSTIFIED` rule, both tested                                                    |
| Privacy manifest incorrect → upload rejection      | Medium                | Test the actual upload path in S15; the manifest is machine-checked by Apple at upload, so failures are fast and unambiguous                          |
| Free macOS minutes exhausted                       | Low                   | Nightly runs on the **public** repo where GitHub Actions macOS is unmetered                                                                           |

---

## 7. Deliverables

- iOS generator sharing the S04 architecture, asset pipeline, and golden harness
- `shells/ios` parameterised as a template, still standalone-buildable
- Correct `Info.plist`, entitlements, and privacy manifest generation
- `build.sh` in generated projects (foundation for source export)
- Nightly free iOS verification builds
- `docs/adr/0005-xcode-project-generation.md`
- `SPRINT-05_REVIEW.md`
