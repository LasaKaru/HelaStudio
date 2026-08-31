# Sprint 10 — Plugin System

|                   |                      |
| ----------------- | -------------------- |
| **Weeks**         | 21–22                |
| **Phase**         | 1 — Product          |
| **Capacity**      | 55 h (38 h new work) |
| **Depends on**    | S04, S05, S09        |
| **Blocks**        | S12, S18             |
| **Planned spend** | $0                   |

---

## 1. Sprint goal

Build the manifest-driven plugin architecture — the mechanism that lets one person maintain 40 plugins — and prove it with three real plugins end to end.

⚠️ **The governing rule, from master spec §13.7: one manifest, seven generated outputs, and a plugin never modifies shell core code.** Violate that rule and the system becomes unmaintainable by plugin #15. Enforce it structurally, not by intention.

---

## 2. Exit criteria

- [ ] Plugin manifest schema published and validated
- [ ] Build-time injection generating all seven outputs from one manifest
- [ ] ⚠️ Conflict detection at **config-save time**, not build time
- [ ] Three plugins working end to end on both platforms: haptics, QR scanner, biometrics
- [ ] Privacy manifest and Data Safety fragments merged automatically
- [ ] Per-plugin binary size delta measured and exposed via the API
- [ ] All-pairs plugin combination matrix running nightly
- [ ] Coverage ≥ 85% line / 80% branch on the plugin subsystem

---

## 3. Task breakdown

| ID     | Task                                                       | Est.     | Priority |
| ------ | ---------------------------------------------------------- | -------- | -------- |
| T-10.1 | Manifest schema and registry                               | 6 h      | P0       |
| T-10.2 | Build-time injection (seven outputs)                       | 10 h     | P0       |
| T-10.3 | Conflict detection and resolution                          | 6 h      | P0       |
| T-10.4 | Plugin 1: haptics (the simplest possible end-to-end proof) | 4 h      | P0       |
| T-10.5 | Plugin 2: QR scanner (camera, permission, native UI)       | 6 h      | P0       |
| T-10.6 | Plugin 3: biometrics (secure, platform-divergent)          | 6 h      | P0       |
|        | **Total**                                                  | **38 h** |          |

---

## 4. Task detail

### T-10.1 — Manifest schema and registry (6 h)

Implement the schema from master spec Appendix A as a validated JSON Schema, with a plugin registry service.

**Manifest structure recap:** `id`, `version`, `capabilities[]`, `configSchema`, per-platform `dependencies`/`permissions`/`plist`/`entitlements`/`proguard`/`sources`, `privacyManifest`, `dataSafety`, `conflicts[]`, `web.typings`.

**Registry requirements:**

- Plugins are **versioned artifacts**, resolved to exact versions in a lockfile stored on the config version. ⚠️ Without a lockfile, two builds of the same config can pull different plugin versions and produce different apps — which breaks reproducibility and, worse, breaks the build cache silently.
- Registry validates every manifest at load; ⚠️ **an invalid manifest must fail startup**, not fail at some customer's build three weeks later.
- `GET /v1/plugins` exposes the catalogue with config schemas so the studio can render forms generically (S12 depends on this).
- **Compatibility matrix** per plugin: min shell version, min OS version, min/max toolchain.

**Acceptance criteria:** three manifests validate; an invalid manifest fails startup with a precise message; the lockfile pins exact versions and is stored with the config version.

**Tests:** `TC-S10-PLG-001` … `TC-S10-PLG-008`

---

### T-10.2 — Build-time injection (10 h)

**The seven outputs generated from each manifest:**

| #   | Output               | Android                                                                 | iOS                                                             |
| --- | -------------------- | ----------------------------------------------------------------------- | --------------------------------------------------------------- |
| 1   | Dependencies         | `build.gradle.kts` deps + `libs.versions.toml` entries                  | `Podfile` entries or SPM packages in `project.yml`              |
| 2   | Manifest fragments   | `AndroidManifest.xml` merge (permissions, features, queries, providers) | `Info.plist` keys + usage strings                               |
| 3   | Entitlements         | —                                                                       | `.entitlements` merge                                           |
| 4   | Privacy declarations | Data Safety fragment                                                    | ⚠️ `PrivacyInfo.xcprivacy` API-reason and tracking-domain merge |
| 5   | Obfuscation rules    | ProGuard/R8 rules                                                       | —                                                               |
| 6   | Bridge registration  | Generated Kotlin registry entry                                         | Generated Swift registry entry                                  |
| 7   | Web typings          | Merged into `@shellwright/bridge` types                                 | same                                                            |

**Merge semantics — where the difficulty actually lives:**

- ⚠️ **Permissions union, deduplicated, sorted.** Two plugins requesting `CAMERA` must produce one declaration.
- ⚠️ **Usage strings:** if two plugins both need `NSCameraUsageDescription`, they conflict on wording. Resolution: the _config_ supplies the string; plugins declare only that it is **required**. This inverts the obvious design and is the correct one — the customer's wording is what App Review reads.
- ⚠️ **Dependency version conflicts:** a shared **BOM** (Firebase BoM, AndroidX, Kotlin) that all plugins must conform to. A plugin declaring a version outside the BOM range fails validation at registry load, not at build.
- **ProGuard rules** concatenated with a header comment naming the source plugin — makes debugging a stripped release build tractable.
- **Bridge registry generated, not reflective** — reflection breaks under R8 full mode and costs startup time.

**Size measurement:** build with and without each plugin nightly; record the delta. ⚠️ Expose it in the API so the studio can show "+1.2 MB" next to each plugin toggle in S12. No competitor does this, and it makes users trust the platform.

**Acceptance criteria:** enabling a plugin produces a buildable project on both platforms; two plugins needing the same permission produce one declaration; privacy manifest merges correctly; size deltas recorded.

**Tests:** `TC-S10-PLG-009` … `TC-S10-PLG-026`

---

### T-10.3 — Conflict detection and resolution (6 h)

⚠️ **Detect at config-save time.** A user must never wait eight minutes for a build to discover that two plugins are incompatible.

**Checks, all run during S01 validation:**

| Check                                                               | Diagnostic                              |
| ------------------------------------------------------------------- | --------------------------------------- |
| Explicitly declared mutual conflict                                 | `CFG_PLUGIN_CONFLICT`                   |
| Duplicate underlying SDK (two scanner plugins both bundling ML Kit) | `CFG_PLUGIN_SDK_DUPLICATE`              |
| Required min-SDK exceeds config's minSdk                            | `CFG_PLUGIN_MIN_SDK`                    |
| Required iOS deployment target exceeds config's                     | `CFG_PLUGIN_IOS_TARGET`                 |
| Dependency version outside the BOM                                  | `CFG_PLUGIN_BOM_VIOLATION`              |
| Entitlement collision requiring incompatible values                 | `CFG_PLUGIN_ENTITLEMENT_CONFLICT`       |
| Plugin requires a third-party licence the org hasn't recorded       | `CFG_PLUGIN_LICENCE_REQUIRED` (warning) |
| Cumulative estimated size exceeds a configured budget               | `CFG_SIZE_BUDGET` (warning)             |

Every diagnostic must name **both** plugins and state the resolution, e.g. _"`qr-scanner` and `scandit-scanner` both provide camera scanning. Remove one."_

**Acceptance criteria:** every check has a passing and a failing test; conflicting configs are rejected at save with an actionable message; no conflicting configuration can reach a runner.

**Tests:** `TC-S10-PLG-027` … `TC-S10-PLG-042`

---

### T-10.4 — Plugin 1: haptics (4 h)

Deliberately the simplest plugin, chosen to prove the pipeline with minimal domain complexity.

- **API:** `sw.haptics.impact('light'|'medium'|'heavy')`, `sw.haptics.notification('success'|'warning'|'error')`, `sw.haptics.selection()`
- **Android:** `VibratorManager` / `HapticFeedbackConstants`; ⚠️ graceful no-op on devices without a vibrator, and respect the system haptics setting
- **iOS:** `UIImpactFeedbackGenerator` etc.; ⚠️ **prepare the generator before use** or the first haptic is noticeably delayed — a small detail that separates "feels native" from "feels off"
- **No permissions, no dependencies, no plist entries** — the ideal first plugin
- Full contract fixtures per S09's rule

**Acceptance criteria:** works on both platforms; no measurable startup or size impact; contract fixtures pass on all three implementations.

**Tests:** `TC-S10-PLG-043` … `TC-S10-PLG-048`

---

### T-10.5 — Plugin 2: QR scanner (6 h)

Exercises camera permission, native UI presentation, and a real third-party dependency.

- **API:** `sw.qrScanner.scan({ formats?, prompt? })` → `{ format, value }`; `scanContinuous` with an event stream
- **Android:** ML Kit barcode scanning + CameraX; permission requested **at call time**, never at launch
- **iOS:** `AVCaptureMetadataOutput` (no third-party dependency needed — smaller and fewer privacy obligations than ML Kit)
- ⚠️ **Permission denial must resolve with a typed error, never hang.** Include a `PERMISSION_DENIED_PERMANENTLY` variant carrying a hint to open Settings — the difference between a support ticket and a self-service fix.
- Torch toggle, scan-region overlay, cancel affordance
- ⚠️ Declares `NSCameraUsageDescription` as **required**; the string comes from config (per T-10.2)
- Privacy manifest: camera access declared; Data Safety: no data collected

**Acceptance criteria:** scans a QR code on both platforms; denial returns a typed error; permanent denial is distinguishable; permission is only requested on first use.

**Tests:** `TC-S10-PLG-049` … `TC-S10-PLG-058`

---

### T-10.6 — Plugin 3: biometrics (6 h)

Exercises secure storage and significant platform divergence.

- **API:** `sw.biometric.isAvailable()`, `sw.biometric.authenticate({ reason })`, `sw.biometric.storeSecret({key, value})`, `sw.biometric.getSecret({key})`
- **Android:** `BiometricPrompt` + `EncryptedSharedPreferences` / Keystore-backed keys
- **iOS:** `LAContext` + Keychain with `.biometryCurrentSet` access control
- ⚠️ **`.biometryCurrentSet` invalidates stored secrets when the user enrols a new face or finger.** This is correct security behaviour and it _will_ generate confused support tickets. Handle it explicitly: return `BIOMETRY_CHANGED` so the site can prompt for a password re-login, and document it prominently.
- ⚠️ **The bridge never returns raw secrets to JS by default.** Prefer an "unlock a server session" pattern: biometry gates the release of a token the site already holds. Document the threat model — a compromised page should not be able to exfiltrate a stored password.
- Handle: no hardware, not enrolled, locked out (too many attempts), user cancelled — each a distinct typed error.

**Acceptance criteria:** authenticate and secret storage work on both platforms; all six error states are distinguishable and tested; enrolling a new biometric produces `BIOMETRY_CHANGED`.

**Tests:** `TC-S10-PLG-059` … `TC-S10-PLG-070`, `TC-S10-SEC-001`

---

## 5. Test cases (selected detail)

| ID               | Type         | Precondition                                   | Steps                                                     | Expected                                                                                        |
| ---------------- | ------------ | ---------------------------------------------- | --------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `TC-S10-PLG-005` | Unit         | Manifest missing a required field              | Load registry                                             | Startup fails naming the file and the field                                                     |
| `TC-S10-PLG-014` | Golden       | Config enabling qr-scanner + biometric         | Generate Android project                                  | One `CAMERA` permission; both plugins' ProGuard rules present with source headers               |
| `TC-S10-PLG-018` | Golden       | Same config                                    | Generate iOS project                                      | `PrivacyInfo.xcprivacy` contains merged reasons; `NSCameraUsageDescription` sourced from config |
| `TC-S10-PLG-022` | Nightly      | Build with and without each plugin             | Compare artifact sizes                                    | Deltas recorded and exposed via the API                                                         |
| `TC-S10-PLG-029` | Unit         | Config with two conflicting scanners           | Save config                                               | 422 `CFG_PLUGIN_SDK_DUPLICATE` naming both                                                      |
| `TC-S10-PLG-033` | Unit         | Plugin needs minSdk 26, config sets 24         | Save config                                               | 422 `CFG_PLUGIN_MIN_SDK` stating both numbers                                                   |
| `TC-S10-PLG-052` | Instrumented | Camera permission denied                       | `sw.qrScanner.scan()`                                     | Rejects `PERMISSION_DENIED`; no hang                                                            |
| `TC-S10-PLG-053` | Instrumented | Permission denied permanently                  | `sw.qrScanner.scan()`                                     | Rejects `PERMISSION_DENIED_PERMANENTLY` with a settings hint                                    |
| `TC-S10-PLG-063` | Instrumented | Secret stored, then a new fingerprint enrolled | `getSecret`                                               | Rejects `BIOMETRY_CHANGED`; stored secret invalidated                                           |
| `TC-S10-PLG-067` | Instrumented | Biometry locked out after failed attempts      | `authenticate`                                            | Rejects `BIOMETRY_LOCKOUT`                                                                      |
| `TC-S10-SEC-001` | Instrumented | Secret stored                                  | Inspect app private storage on a rooted/jailbroken device | Secret not readable in plaintext                                                                |
| `TC-S10-PLG-070` | Nightly      | All-pairs matrix over 3 plugins                | Build every pair on both platforms                        | All succeed                                                                                     |

---

## 6. Risks

| Risk                                            | Likelihood | Impact | Mitigation                                                                                                                                                                                                                                                              |
| ----------------------------------------------- | ---------- | ------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ A plugin needs a shell core change           | **High**   | High   | When it happens, add a **shell capability with a flag**, not a plugin-specific hack. Record every instance in the ADR — a pattern of these means the shell's extension points are wrong and need redesign, which is far cheaper to learn at plugin 3 than at plugin 15. |
| Dependency conflicts appear only in combination | **High**   | Medium | All-pairs nightly matrix from this sprint, with only 3 plugins — establishes the harness while it is cheap                                                                                                                                                              |
| Manifest schema too rigid for a future plugin   | Medium     | Medium | Include an `extensions` escape hatch; ⚠️ but require an ADR to use it, so it does not become the default                                                                                                                                                                |
| Biometric edge cases under-tested               | Medium     | High   | Six explicit error states, each with a test. Test on a real device — emulator biometry does not reproduce lockout or enrolment-change behaviour.                                                                                                                        |
| Scope creep into more plugins                   | **High**   | Medium | ⚠️ Three plugins. The system is the deliverable, not the catalogue. S18 adds the other twelve.                                                                                                                                                                          |

---

## 7. Deliverables

- Plugin manifest schema, registry, and lockfile mechanism
- Build-time injection producing all seven outputs on both platforms
- Config-time conflict detection with eight checks and actionable diagnostics
- Three production-quality plugins with full contract fixtures
- Per-plugin size-delta measurement exposed via the API
- All-pairs nightly matrix harness
- `docs/reference/plugin-authoring.md` (foundation for the public plugin SDK in Phase 3)
- `SPRINT-10_REVIEW.md`
