# Sprint 07 — Build Orchestration (Temporal + Linux Runner)

|                   |                               |
| ----------------- | ----------------------------- |
| **Weeks**         | 15–16                         |
| **Phase**         | 1 — Pipeline                  |
| **Capacity**      | 55 h (38 h new work)          |
| **Depends on**    | S04, S06                      |
| **Blocks**        | S08 — ⚠️ on the critical path |
| **Planned spend** | $0 (Oracle free tier)         |

---

## 1. Sprint goal

A build is a long, failure-prone, resumable, cancellable, metered operation. Build the orchestration layer that makes it reliable — Temporal workflows, an isolated Linux runner, live log streaming, and Android builds triggered from the API with no human involvement.

---

## 2. Exit criteria

- [ ] `POST /v1/apps/{app}/builds` → signed-or-unsigned Android AAB in R2, with zero manual steps
- [ ] Build state machine correct: queued → generating → building → verifying → succeeded/failed/cancelled
- [ ] Logs stream live to a WebSocket client and are archived to R2
- [ ] ⚠️ Cancellation frees the runner within 5 seconds
- [ ] Workflow survives an orchestrator restart mid-build (proven by test)
- [ ] Every build runs in a fresh container, destroyed after; no cross-tenant reuse
- [ ] Usage metered by the runner, not the API
- [ ] p95 Android build < 6 min cold, < 3 min warm-cache

---

## 3. Task breakdown

| ID     | Task                                                | Est.     | Priority |
| ------ | --------------------------------------------------- | -------- | -------- |
| T-07.1 | Temporal workflow design and implementation         | 9 h      | P0       |
| T-07.2 | Linux runner: container image and execution sandbox | 8 h      | P0       |
| T-07.3 | Build activities: generate, build, verify, upload   | 8 h      | P0       |
| T-07.4 | Log streaming pipeline                              | 6 h      | P0       |
| T-07.5 | Build API, state machine, and metering              | 7 h      | P0       |
|        | **Total**                                           | **38 h** |          |

---

## 4. Task detail

### T-07.1 — Temporal workflow design (9 h)

**Why Temporal rather than a queue:** builds run 3–15 minutes, fail transiently, must survive deploys, must be cancellable, and must never double-charge. Implementing durable execution, retries with backoff, heartbeats, and compensation by hand is a multi-month detour. Temporal OSS runs on the Oracle host for free.

**Workflow:**

```csharp
[Workflow]
public class BuildWorkflow {
    [WorkflowRun]
    public async Task<BuildResult> RunAsync(BuildRequest req) {
        // 1. cheap validation on the orchestrator — never burn a runner on a bad config
        var validation = await Workflow.ExecuteActivityAsync(
            (BuildActivities a) => a.ValidateAsync(req), Short);
        if (!validation.IsValid) return BuildResult.Invalid(validation);

        // 2. cache lookup by the S01 hash split
        var cached = await Workflow.ExecuteActivityAsync(
            (BuildActivities a) => a.LookupCacheAsync(req.Hashes), Short);
        if (cached is not null) return BuildResult.Cached(cached);

        // 3. lease a runner (heartbeated activity)
        // 4. generate  5. build  6. verify  7. upload  8. record usage
        // finally: release runner  (compensation, always runs)
    }
}
```

**Design rules:**

- ⚠️ **Workflow code must be deterministic.** No `DateTime.Now`, no `Guid.NewGuid()`, no direct I/O in workflow code — only in activities. Temporal replays workflow code on recovery; nondeterminism causes replay failures that are miserable to debug.
- **Heartbeat** the long build activity every 10 s with progress. Heartbeat timeout 60 s → a dead runner is detected in a minute, not when the build times out.
- **Retry policy:** retry infrastructure failures (runner unreachable, R2 5xx) up to 3× with exponential backoff. ⚠️ **Never retry a compilation failure** — it will fail identically and costs money. Distinguish the two with typed, non-retryable exception classes.
- **Cancellation:** a cancellation signal triggers the compensation path; the activity receives cancellation, kills the container, and releases the lease.
- **Timeouts:** activity start-to-close 20 min Android / 45 min iOS; workflow execution timeout 60 min.
- **Versioning:** use `Workflow.Patched` from the first change onward — you will change the workflow while builds are in flight.

**Acceptance criteria:** workflow runs end-to-end; killing the worker mid-build resumes correctly on restart; cancellation releases the runner in < 5 s.

**Tests:** `TC-S07-BLD-001` … `TC-S07-BLD-010`

---

### T-07.2 — Linux runner: image and sandbox (8 h)

**Container image (arm64 for the Oracle host — verified in S00):**

```dockerfile
FROM eclipse-temurin:21-jdk-jammy
# Android SDK cmdline-tools, platforms, build-tools — baked in, never installed at build time
ENV ANDROID_HOME=/opt/android-sdk
RUN <install cmdline-tools, accept licenses, install platform-tools, platforms;android-36, build-tools>
# Pre-warm Gradle: run a throwaway build so the distribution and common deps are cached in the image
COPY warmup-project /tmp/warmup
RUN cd /tmp/warmup && ./gradlew assembleDebug --no-daemon && rm -rf /tmp/warmup
USER 10001:10001
```

**⚠️ Isolation requirements (non-negotiable, from `01_ENGINEERING_STANDARDS.md` §6):**

- One container per build, destroyed after. **Never reuse across tenants.**
- Non-root user; `--read-only` root filesystem with explicit `tmpfs` for scratch
- `--cap-drop=ALL`, `--security-opt=no-new-privileges`
- CPU and memory limits (⚠️ critical on a 12 GB host — an unbounded Gradle daemon will OOM Postgres)
- **Network egress allowlist:** Maven Central, Google Maven, your R2 endpoint. Nothing else. A malicious plugin or a compromised dependency must not be able to exfiltrate.
- Per-app cache volume mounted at `~/.gradle/caches`, keyed by `codeKey`; **never a shared mutable cache**

**Gradle build performance:**

```properties
org.gradle.caching=true
org.gradle.configuration-cache=true
org.gradle.parallel=true
org.gradle.jvmargs=-Xmx2g -XX:MaxMetaspaceSize=512m
kotlin.incremental=false          # ⚠️ pointless in ephemeral containers, costs time
android.enableR8.fullMode=true
```

Plus a **remote Gradle build cache backed by R2** (via an HTTP cache node) — shared _read-only_ across tenants for public dependencies only, never for compiled app code.

**Acceptance criteria:** image builds and is < 3 GB; a container starts and is ready in < 15 s; egress to an unlisted host is refused; a build cannot write outside its scratch mount.

**Tests:** `TC-S07-BLD-011` … `TC-S07-BLD-016`, `TC-S07-SEC-001`, `TC-S07-SEC-002`

---

### T-07.3 — Build activities (8 h)

| Activity             | Responsibility                                                     | Notes                                                                    |
| -------------------- | ------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| `ValidateAsync`      | Re-run S01 validation server-side                                  | ⚠️ Never trust the client's validation                                   |
| `LookupCacheAsync`   | Query artifacts by `codeKey`/`assetKey`/`contentKey`               | Implements the split-key fast paths                                      |
| `LeaseRunnerAsync`   | Acquire a runner slot via a Redis lease with TTL                   | TTL renewal on heartbeat; expiry frees the slot if the orchestrator dies |
| `GenerateAsync`      | Run the S04 generator, stream a tar into the container             | Uses `IFileSink`                                                         |
| `BuildAsync`         | Execute Gradle in the container, stream logs, heartbeat            | ⚠️ Argument arrays, never string concatenation (injection via app name)  |
| `VerifyAsync`        | `apksigner verify`, manifest sanity, size budget, permission audit | Fails the build on budget breach                                         |
| `UploadAsync`        | Multipart upload of AAB/APK + mapping.txt to R2                    | Content-addressed path                                                   |
| `RecordUsageAsync`   | Write the `usage_records` row                                      | ⚠️ Written by the runner path, so metering survives an API outage        |
| `ReleaseRunnerAsync` | Compensation — always runs                                         | Kills container, releases lease                                          |

**Cache fast paths (the unit-economics feature):**

- `codeKey` hit + `assetKey` and `contentKey` differ → **resource patch**: unzip the cached artifact, replace `res/` and `assets/appconfig.json`, re-zip, re-sign. Target < 60 s versus a 4-minute full build.
- All three hit → return the cached artifact immediately.
- Measure and log the hit rate from day one; it is a headline internal metric.

**Acceptance criteria:** all activities implemented and independently testable; the resource-patch path produces a valid, installable APK; measured hit-path timings recorded.

**Tests:** `TC-S07-BLD-017` … `TC-S07-BLD-028`

---

### T-07.4 — Log streaming pipeline (6 h)

**Architecture (from `01_ENGINEERING_STANDARDS.md` §2.6):**

```
runner stdout/stderr
   → line-framed, structured, correlationId-tagged
   → Redis Stream  build:{id}:logs  (XADD, MAXLEN ~ 50000)
   → API WebSocket fan-out → browser (virtualised renderer)
   → concurrently: multipart append to R2 for the durable record
```

**Requirements:**

- ⚠️ **Never buffer the whole log in memory or store it in Postgres.** A verbose Gradle build produces tens of MB.
- **Secret scrubbing before write, not before display** — Gradle and signing tools print keystore paths and occasionally more. Maintain a redaction filter with a regression test suite of known-leaky tool outputs.
- Backpressure: if no client is connected, still archive; if a client is slow, drop from the live stream (never from the archive) and tell the UI it fell behind.
- Reconnect resumes from the last stream id.
- Classify lines: `error`/`warning`/`info` so the UI can filter — Gradle output is 95% noise and surfacing the three lines that matter is a real usability win.

**Acceptance criteria:** logs appear in the browser within 1 s of emission; a 50 MB log does not raise API memory materially; a fake keystore password in tool output does not appear in the archive.

**Tests:** `TC-S07-BLD-029` … `TC-S07-BLD-034`, `TC-S07-SEC-003`

---

### T-07.5 — Build API, state machine, and metering (7 h)

**Endpoints:**

```
POST   /v1/apps/{app}/builds          {platform, configVersionId?, buildType}  → 202
GET    /v1/builds/{id}
GET    /v1/builds/{id}/logs           (WebSocket, or SSE fallback)
POST   /v1/builds/{id}/cancel
GET    /v1/apps/{app}/builds          cursor-paginated
GET    /v1/builds/{id}/artifacts/{artifactId}   → short-lived signed R2 URL
```

**State machine — every transition audited:**

```
queued ─► generating ─► building ─► verifying ─► succeeded
   │           │             │           │
   └───────────┴─────────────┴───────────┴──► failed
   └──────────────────────────────────────────► cancelled
```

Illegal transitions must throw, not be silently ignored. Test every illegal transition explicitly.

**Requirements:**

- ⚠️ **Idempotency-Key required** on build creation. A double-clicked button must not produce two builds and two charges.
- ⚠️ **Per-org concurrency limit** enforced before enqueuing. Without it, one user can starve the whole free-tier fleet.
- Quota check before enqueue (the counters exist now; enforcement becomes real in S17).
- Artifact downloads via short-lived (15 min) signed R2 URLs, never proxied through the API — proxying a 200 MB IPA through a free-tier host is a self-inflicted outage.
- `usage_records` written by the runner with `{orgId, buildId, platform, runnerSeconds, cacheHit, artifactBytes}`.

**Acceptance criteria:** duplicate Idempotency-Key returns the same build; concurrency limit enforced; artifact URL expires; every illegal state transition is rejected.

**Tests:** `TC-S07-BLD-035` … `TC-S07-BLD-046`

---

## 5. Test cases (selected detail)

| ID               | Type        | Precondition                                        | Steps                                | Expected                                                                 |
| ---------------- | ----------- | --------------------------------------------------- | ------------------------------------ | ------------------------------------------------------------------------ |
| `TC-S07-BLD-004` | Integration | Build running                                       | Kill and restart the Temporal worker | Build resumes and completes                                              |
| `TC-S07-BLD-006` | Integration | Build running                                       | `POST /cancel`                       | Container killed and lease released in < 5 s; state `cancelled`          |
| `TC-S07-BLD-008` | Integration | Config with a compile error                         | Trigger build                        | Fails once; **no retries** (non-retryable exception)                     |
| `TC-S07-BLD-009` | Integration | R2 returns 503 on upload                            | Trigger build                        | Retries 3× with backoff, then fails with a clear cause                   |
| `TC-S07-BLD-021` | Integration | Successful build cached; change only a theme colour | Trigger build                        | Resource-patch path taken; < 60 s; APK installs and shows the new colour |
| `TC-S07-BLD-026` | Integration | App name `Foo"; rm -rf /`                           | Trigger build                        | Built safely; no shell interpretation (argument arrays)                  |
| `TC-S07-SEC-001` | Integration | Runner container                                    | `curl https://example.com`           | Refused by egress allowlist                                              |
| `TC-S07-SEC-002` | Integration | Runner container                                    | Write outside the scratch mount      | Refused (read-only rootfs)                                               |
| `TC-S07-SEC-003` | Integration | Build whose log contains a fake keystore password   | Read archived log                    | Password redacted                                                        |
| `TC-S07-BLD-036` | Integration | —                                                   | Two POSTs, same Idempotency-Key      | One build; identical response body                                       |
| `TC-S07-BLD-039` | Integration | Org at concurrency limit                            | Trigger another build                | 429 with `Retry-After`                                                   |
| `TC-S07-PRF-001` | Measurement | Cold cache                                          | 10 builds of `maximal.json`          | p95 < 6 min                                                              |
| `TC-S07-PRF-002` | Measurement | Warm cache                                          | 10 builds                            | p95 < 3 min; hit rate logged                                             |

---

## 6. Risks

| Risk                                                                                        | Likelihood | Mitigation                                                                                                                                                    |
| ------------------------------------------------------------------------------------------- | ---------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ⚠️ Temporal learning curve consumes the sprint                                              | **High**   | Timebox T-07.1 to 9 h. Complete Temporal's official .NET tutorial _before_ sprint day 1 — treat it as pre-work, not sprint work.                              |
| Nondeterministic workflow code causes replay failures                                       | Medium     | Temporal's replay test harness runs in CI against recorded histories from the start                                                                           |
| Oracle host (2 OCPU / 12 GB) cannot run Temporal + Postgres + a Gradle build simultaneously | **High**   | ⚠️ Explicit per-container memory limits; if it will not fit, move the runner to a separate €4/mo Hetzner box. Budget for this; do not discover it under load. |
| Log volume overwhelms Redis free tier (10k commands/day on Upstash)                         | **High**   | ⚠️ Batch `XADD` calls; or self-host Redis on the Oracle box instead of Upstash for build logs. Decide in T-07.4, not in production.                           |
| Cache never hits due to a determinism bug                                                   | Medium     | S04's double-generation test plus explicit cache-hit-rate logging from the first build                                                                        |

---

## 7. Deliverables

- `services/orchestrator` — Temporal workflows and activities
- Hardened arm64 Linux runner image with egress allowlisting
- Build API with idempotency, concurrency limits, and a strict state machine
- Live log streaming with secret scrubbing
- Cache fast paths implemented and measured
- Usage metering written from the runner
- `SPRINT-07_REVIEW.md` with measured build times and cache hit rate
