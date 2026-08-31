# Sprint 08 — macOS Runner, iOS Cloud Builds & Artifact Store

|                   |                                     |
| ----------------- | ----------------------------------- |
| **Weeks**         | 17–18                               |
| **Phase**         | 1 — Pipeline                        |
| **Capacity**      | 55 h (38 h new work)                |
| **Depends on**    | S05, S07                            |
| **Blocks**        | S13, S15                            |
| **Planned spend** | $0–$20 (Codemagic overage possible) |

---

## 1. Sprint goal

Extend the orchestrator to produce iOS builds on macOS capacity, and prove the unit economics: **measure the real cost per iOS build with caching on**, and validate the split-hash cache fast paths that master spec §16 depends on.

⚠️ **M2 milestone gate.** After this sprint, a config posted to the API produces both a signed AAB and a signed IPA with no human involvement.

---

## 2. Exit criteria

- [ ] `POST /builds {platform: ios}` produces a signed IPA in R2 with no manual steps
- [ ] macOS capacity abstracted behind a provider interface — Codemagic today, self-hosted later, no orchestrator changes required
- [ ] Split-hash cache implemented for both platforms; asset-only change completes in < 90 s
- [ ] Measured, documented cost per iOS build (cold and warm) recorded in `COSTS.md`
- [ ] Artifact lifecycle: retention policy, signed URLs, garbage collection
- [ ] Toolchain descriptor supports N and N−1 Xcode with per-app pinning
- [ ] p95 iOS build < 15 min cold, < 8 min warm
- [ ] Nightly matrix builds both platforms across 5 fixtures

---

## 3. Task breakdown

| ID     | Task                                             | Est.     | Priority |
| ------ | ------------------------------------------------ | -------- | -------- |
| T-08.1 | macOS capacity provider abstraction              | 7 h      | P0       |
| T-08.2 | Codemagic provider implementation                | 8 h      | P0       |
| T-08.3 | iOS build activities and signing plumbing        | 8 h      | P0       |
| T-08.4 | Split-hash cache and fast paths (both platforms) | 8 h      | P0       |
| T-08.5 | Artifact store lifecycle                         | 4 h      | P0       |
| T-08.6 | Cost measurement and toolchain matrix            | 3 h      | P0       |
|        | **Total**                                        | **38 h** |          |

---

## 4. Task detail

### T-08.1 — macOS capacity provider abstraction (7 h)

⚠️ **The single most important design decision of this sprint.** You will start on Codemagic (free), likely move to hosted Macs, and eventually own hardware. The orchestrator must not care.

```csharp
public interface IMacBuildProvider {
    string Name { get; }
    Task<MacCapacityStatus> GetCapacityAsync(CancellationToken ct);
    Task<MacBuildHandle> StartAsync(MacBuildRequest req, CancellationToken ct);
    IAsyncEnumerable<LogLine> StreamLogsAsync(MacBuildHandle h, CancellationToken ct);
    Task<MacBuildOutcome> WaitAsync(MacBuildHandle h, CancellationToken ct);
    Task CancelAsync(MacBuildHandle h, CancellationToken ct);
    Task<Stream> FetchArtifactAsync(MacBuildHandle h, string artifactName, CancellationToken ct);
}
```

Implementations planned: `CodemagicProvider` (now), `GitHubActionsProvider` (free fallback for unsigned verification), `SelfHostedTartProvider` (S25+).

**Provider selection policy** (a small strategy class, configuration-driven):

1. Unsigned verification build → GitHub Actions on the public shell repo (free, unmetered)
2. Signed customer build → Codemagic
3. Provider unavailable or at capacity → queue with a user-visible position, never silently hang

⚠️ **Record an ADR (`0007-macos-capacity.md`)** covering the cost thresholds from master spec §13.4: hosted until ~150 iOS builds/day, owned Mac minis beyond, and never AWS EC2 Mac for bursty work because of the 24-hour minimum dedicated-host billing window.

**Acceptance criteria:** interface implemented; a fake provider drives the full workflow in integration tests without touching real macOS; swapping providers requires no orchestrator change.

**Tests:** `TC-S08-BLD-001` … `TC-S08-BLD-006`

---

### T-08.2 — Codemagic provider implementation (8 h)

**Approach:** Codemagic builds from a git repository, so the orchestrator must deliver the generated project to one.

**Mechanism:**

1. Maintain a **build-staging repository** (private) with one branch per build: `build/{buildId}`.
2. The `GenerateAsync` activity pushes the generated Xcode project to that branch. ⚠️ Use a shallow, single-commit push with a machine account whose token is scoped to that repo only.
3. Trigger the Codemagic build via its REST API, passing the branch and environment variables (bundle id, version, signing group).
4. Poll status; stream logs via the API into the same Redis Stream pipeline from S07.
5. Fetch the IPA artifact and re-upload to R2 (content-addressed).
6. ⚠️ **Delete the branch** in the compensation step. Generated projects contain the customer's config; leaving hundreds of branches around is both a mess and a data-retention problem.

**Alternative worth evaluating in the ADR:** Codemagic supports building from an uploaded archive in some configurations, which would avoid the staging repo entirely. Try it first — if it works, it is simpler and safer. Timebox the evaluation to 1 hour.

**⚠️ Free-tier constraints to design around:**

- 500 macOS minutes/month, **1 concurrency**, personal account only (teams get no free minutes)
- Therefore: queue iOS builds strictly, surface queue position in the API, and ⚠️ **track remaining minutes** as a first-class metric with an alert at 80%
- If the student/education application from S00 was granted, re-measure — the ceiling may be gone

**Acceptance criteria:** end-to-end signed IPA produced from an API call; logs stream live; cancellation stops the remote build; staging branch deleted; minutes consumed are tracked.

**Tests:** `TC-S08-BLD-007` … `TC-S08-BLD-016`

---

### T-08.3 — iOS build activities and signing plumbing (8 h)

**Activities mirroring S07's Android set:**

| Activity              | iOS specifics                                                                                                                             |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| `GenerateIosAsync`    | S05 generator; run XcodeGen; run `pod install` only if a plugin requires it                                                               |
| `ResolveSigningAsync` | Fetch certificate + profile via App Store Connect API using **your own** credentials for now; the customer-credential path arrives in S14 |
| `BuildIosAsync`       | `xcodebuild archive` → `exportArchive` with the correct `ExportOptions.plist`                                                             |
| `VerifyIosAsync`      | `codesign --verify --deep --strict`, entitlement dump, size budget, ⚠️ **privacy-manifest presence check**                                |
| `UploadIosAsync`      | IPA + dSYM to R2                                                                                                                          |

**⚠️ Signing hygiene, established now even though customer keys arrive in S14:**

- Signing material is injected as encrypted environment variables at job start, never committed, never logged.
- The redaction filter from S07 T-07.4 must be extended with Apple-specific patterns (`.p8` contents, `security` command output, keychain dumps). Add a regression test with a captured sample of real (fake-keyed) tool output.
- ⚠️ **A build must fail closed if signing material is missing** — never silently produce an unsigned artifact and label it signed.

**Common failure modes to handle explicitly with typed, non-retryable errors and actionable messages:**
| Failure | User-facing message must say |
|---|---|
| Profile does not match bundle id | Which bundle id the profile covers vs which the config requests |
| Certificate expired | Expiry date and how to renew |
| Missing device in ad-hoc profile | Which device UDID is absent |
| `CFBundleVersion` not incremented | The last uploaded value and the required minimum |

These four account for the large majority of real iOS build failures. Handling them well is a visible quality difference from competitors that just surface raw `xcodebuild` output.

**Acceptance criteria:** signed IPA verifies with `codesign`; each of the four failure modes produces its specific diagnostic, tested with deliberately broken inputs.

**Tests:** `TC-S08-BLD-017` … `TC-S08-BLD-028`, `TC-S08-SEC-001`

---

### T-08.4 — Split-hash cache and fast paths (8 h)

⚠️ **This task determines whether the business is viable.** Master spec §16 assumes 70–80% of builds avoid a full compile.

**Implementation per platform:**

| Scenario                                 | Android path                                       | iOS path                                      | Target   |
| ---------------------------------------- | -------------------------------------------------- | --------------------------------------------- | -------- |
| All three keys hit                       | Return cached artifact                             | Return cached artifact                        | < 5 s    |
| `codeKey` hit, `assetKey` changed        | Unzip AAB/APK, replace `res/`, re-zip, **re-sign** | ⚠️ Replace `Assets.car`, re-sign — see caveat | < 90 s   |
| `codeKey` hit, `contentKey` changed only | Replace `assets/appconfig.json`, re-sign           | Replace the config resource, re-sign          | < 60 s   |
| `codeKey` changed                        | Full build                                         | Full build                                    | 4–15 min |

⚠️ **iOS caveat to investigate and record honestly:** re-signing a modified IPA is well-established, but replacing a compiled asset catalogue (`Assets.car`) requires running `actool` and repackaging, which needs macOS anyway. So the iOS "asset-only" path still consumes macOS minutes — just far fewer (roughly 60–90 s instead of 8 minutes). **Do not claim a zero-Mac path for iOS asset changes.** Measure the real number and put it in `COSTS.md`. If it turns out the saving is small, the honest conclusion is that iOS caching is mostly about `DerivedData` reuse rather than artifact patching — record that.

**Cache storage:** R2, content-addressed at `cache/{platform}/{codeKey}/artifact.{ext}`, with a metadata object recording toolchain and shell versions. ⚠️ Invalidate the entire cache namespace on a shell-version or toolchain bump — a stale cache serving an artifact built against an old SDK is a correctness bug that would surface as mysterious store rejections.

**Metrics to emit from the first build:** hit rate by key type, time saved, macOS minutes saved. These belong on a dashboard you look at weekly.

**Acceptance criteria:** each fast path produces a valid, installable, correctly-signed artifact; measured timings meet targets or the targets are revised with evidence; shell-version bump invalidates the cache.

**Tests:** `TC-S08-BLD-029` … `TC-S08-BLD-040`

---

### T-08.5 — Artifact store lifecycle (4 h)

**Requirements:**

- Content-addressed layout: `artifacts/{orgId}/{appId}/{buildId}/{filename}`; cache under a separate prefix.
- **Retention policy** (configurable per plan, enforced by a scheduled Temporal workflow):
  | Artifact | Free | Paid |
  |---|---|---|
  | Build artifacts (AAB/IPA) | 7 days | 90 days |
  | Build logs | 30 days | 1 year |
  | dSYM / mapping.txt | ⚠️ **retain as long as the app version exists** — needed to symbolicate crashes | same |
  | Cache entries | 30 days idle | 30 days idle |
- ⚠️ **Garbage collection must be reference-counted, not blind TTL.** A cache entry still referenced by a recent build must survive. Sweep with a mark phase, then delete.
- Short-lived (15 min) signed download URLs; never proxy large files through the API.
- Storage usage per org tracked for S17 billing.

**Acceptance criteria:** GC deletes expired artifacts and never deletes a referenced one; signed URL expires correctly; storage usage is accurate.

**Tests:** `TC-S08-BLD-041` … `TC-S08-BLD-046`

---

### T-08.6 — Cost measurement and toolchain matrix (3 h)

**Measurement — the real deliverable of this sprint:**

Run 20 builds per platform across cold, warm, asset-only, and config-only scenarios. Record in `docs/perf/build-economics.md`:

| Scenario               | Wall clock | macOS minutes | Cost @ $0.095/min | Cache key hit    |
| ---------------------- | ---------- | ------------- | ----------------- | ---------------- |
| iOS cold               |            |               |                   | none             |
| iOS warm (DerivedData) |            |               |                   | codeKey          |
| iOS asset-only         |            |               |                   | codeKey          |
| iOS config-only        |            |               |                   | codeKey+assetKey |
| Android cold           |            | n/a           | ~$0.01            | none             |
| Android asset-only     |            | n/a           | ~$0.003           | codeKey          |

⚠️ **Then compare against master spec §16 and update it.** If iOS builds cost $0.60 rather than the estimated $0.30, the free tier's 15 iOS builds/month costs $9/user/month and the pricing in §17 needs revising. **Better to learn this in week 18 than after launch.**

**Toolchain matrix:** the `ToolchainDescriptor` gains an `xcodeVersion`; the nightly runs both N and N−1. Per-app pinning is exposed in the API (studio UI arrives in S12).

**Acceptance criteria:** economics document written; master spec §16 and §17 reconciled with measured reality; nightly builds both Xcode versions.

**Tests:** `TC-S08-BLD-047`, `TC-S08-BLD-048`

---

## 5. Test cases (selected detail)

| ID               | Type        | Precondition                                        | Steps                 | Expected                                                                   |
| ---------------- | ----------- | --------------------------------------------------- | --------------------- | -------------------------------------------------------------------------- |
| `TC-S08-BLD-003` | Integration | Fake provider registered                            | Run full iOS workflow | Completes without touching real macOS                                      |
| `TC-S08-BLD-011` | Integration | Build running on Codemagic                          | Cancel via API        | Remote build cancelled; staging branch deleted; minutes stop accruing      |
| `TC-S08-BLD-014` | Integration | Codemagic returns 500                               | Trigger build         | Retries with backoff; surfaces provider error after 3 attempts             |
| `TC-S08-BLD-019` | Integration | Profile for a different bundle id                   | Trigger build         | Fails with `IOS_PROFILE_BUNDLE_MISMATCH` naming both ids; not retried      |
| `TC-S08-BLD-022` | Integration | No signing material supplied                        | Trigger release build | Fails closed; ⚠️ no unsigned artifact produced or labelled                 |
| `TC-S08-BLD-031` | Integration | Cached Android build; change only theme colour      | Trigger build         | Resource-patch path; < 90 s; installs; new colour visible; signature valid |
| `TC-S08-BLD-036` | Integration | Cached iOS build; change only `initialUrl`          | Trigger build         | Config-only path; measured and recorded; IPA verifies                      |
| `TC-S08-BLD-038` | Integration | Cache populated; bump shell version                 | Trigger build         | Cache miss; full build                                                     |
| `TC-S08-BLD-043` | Integration | Expired artifact still referenced by a recent build | Run GC                | Not deleted                                                                |
| `TC-S08-SEC-001` | Integration | Build log containing simulated `security` output    | Read archive          | Certificate/key material redacted                                          |
| `TC-S08-PRF-001` | Measurement | Cold cache                                          | 10 iOS builds         | p95 < 15 min                                                               |
| `TC-S08-PRF-002` | Measurement | Warm cache                                          | 10 iOS builds         | p95 < 8 min                                                                |

---

## 6. Risks

| Risk                                                        | Likelihood        | Impact | Mitigation                                                                                                                                                                                                                |
| ----------------------------------------------------------- | ----------------- | ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ **Measured iOS cost invalidates the free-tier pricing**  | **Medium**        | High   | This sprint exists partly to find out. If it does, revise master spec §17 immediately — reduce free iOS builds, or shift the free tier toward Android-unlimited plus a small iOS allowance. Better now than after launch. |
| Codemagic 1-concurrency free tier bottlenecks alpha testing | **High**          | Medium | Queue with visible position; use free GitHub Actions macOS for unsigned verification; pay ~$0.095/min for the handful of overage builds — budget $20                                                                      |
| Staging-repo approach leaks customer config                 | Medium            | High   | Private repo, scoped machine token, branch deleted in compensation, retention audit. Evaluate the archive-upload alternative first.                                                                                       |
| iOS asset-fast-path saves less than hoped                   | **Medium**        | Medium | Measure honestly; adjust the economics rather than the measurement. iOS caching may be mostly `DerivedData`, which is still a real saving.                                                                                |
| Toolchain bump breaks everything at once                    | Certain, annually | Medium | N and N−1 in nightly; per-app pinning; canary before fleet migration                                                                                                                                                      |

---

## 7. Deliverables

- `IMacBuildProvider` abstraction with a Codemagic implementation and a fake for tests
- End-to-end signed iOS builds from the API
- Split-hash cache with measured fast paths on both platforms
- Artifact lifecycle with reference-counted GC
- `docs/perf/build-economics.md` — **the measured numbers that validate or correct the business plan**
- `docs/adr/0007-macos-capacity.md`
- Updated master spec §16/§17 if measurements demand it
- `SPRINT-08_REVIEW.md` — **M2 milestone gate assessment**
