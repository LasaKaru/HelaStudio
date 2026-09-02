# Sprint 07 review — build orchestration

**Goal:** turn a stored configuration into a signed Android artifact, durably,
cancellably, and cheaply enough that most builds do not run a compiler.

## Exit criteria

| Criterion                                                      | Status                                                            |
| -------------------------------------------------------------- | ----------------------------------------------------------------- |
| Temporal workflow with retries, cancellation, and compensation | ✅ against a real Temporal dev server                             |
| Builds run in a fresh, isolated environment, destroyed after   | ⚠️ **argument-level only** — no container runtime here            |
| A cancelled build frees its runner within five seconds         | ✅ asserted; the compensation path is uncancellable by design     |
| Three-way cache key with a working fast path                   | ✅ and the outcome names are now held to what the code does       |
| Live build logs, with credentials scrubbed before storage      | ✅ against a real Redis, driven by a corpus of real tool output   |
| Build API: start, watch, cancel, download                      | ✅ six endpoints, `Idempotency-Key` required on start             |
| Metering that survives retries                                 | ✅ unique index on `build_id`, `ON CONFLICT DO NOTHING`           |
| Tenant isolation over the build tables                         | ✅ 8 tests; `BYPASSRLS` fails six of them                         |
| **Android APK produced end to end**                            | ❌ **not done** — no Android SDK in this environment              |
| Measured build times and cache hit rate                        | ⚠️ **partial** — the parts this repo owns are measured; see below |

**720 .NET tests** (187 new: 151 orchestrator, 36 API) and **241 TypeScript
tests**, all green. Programme total **1,134**.

## What shipped

### The cache fast path is real, and says what it does

`Miss`, `Warm`, `Patch`, `Complete`. `Warm` is a full toolchain run against a
warm dependency cache and exists as a separate value only so metering can tell
it from a cold one. `Patch` replaces one uncompiled JSON entry in a cached APK
and re-signs: **0.51 s for a 20 MB archive**.

⚠️ **The first version of this claimed a patch and ran a full build.** It
reported `WasPatched: true` after a four-minute compile. Metering, queue
estimates and the customer's bill are computed from that flag, so an outcome
naming a cost nobody paid is worse than no cache at all.

### The orchestrator has its own database role

`shellwright_runner`: total reach over tenants, six tables, no `BYPASSRLS`. It
cannot read a user, an organisation, a membership, an asset or any credential
table — refused by a missing grant rather than by convention.

⚠️ Every runner policy is scoped `TO shellwright_runner`, and that clause is the
whole safety of it. Permissive policies are OR'd, so one `USING (true)` without
it would hand the API's role every tenant's rows with every other test still
green. Removing it from one policy makes a test fail by name.

### Logs go two places that fail independently

An ndjson archive on disk is the record; a bounded Redis stream is the
convenience. Redis missing, unreachable, or failing mid-build degrades to
archive-only and reports how many lines the viewer missed. Redaction happens on
the way in — at render time the secret would already be in both.

### Idempotency is an index, not a read

`Idempotency-Key` is required on builds and optional everywhere else, because a
retried build costs runner minutes somebody is billed for. Removing the
unique-violation recovery makes four concurrent identical requests produce a
500, which is the test.

## Defects the tests caught before a human would have

- **The API's `BuildState` invented two states and renumbered the terminal
  ones.** The orchestrator writes that integer straight into the column, so it
  would not have failed — it would have left every successful build with no
  finish time and recorded every cancelled build as **succeeded**.
  `BuildContractTests` now holds the enums equal in name and number.
- **The terminal set was a literal `IN (6, 7, 8)` in SQL** that no compiler
  checks, and it was wrong. It is computed in C# and passed as a flag.
- **The archive's `StreamWriter` buffered its last kilobyte**, so a crash lost
  exactly the lines explaining the crash.
- **A configuration load joined through `workspaces`** to resolve an
  organisation nothing read. The runner role has no grant there, so every load
  failed with "permission denied". Fixed by deleting the unused field rather
  than widening the role — the tempting direction and the wrong one.
- **`IArtifactCache` ignored the build type**, with a placeholder returning
  `Debug` and a comment promising to fix it later. A debug-signed artifact would
  have satisfied a release build. The value was at the call site all along.
- **A traversal test never awaited its assertions**, so it could not fail.
- **A turbo cache key that silently deleted generated code.** The `generate`
  task declared `inputs: ["schema/**"]`, but the API client generates from
  `openapi/**`. Editing the OpenAPI document therefore invalidated nothing, and
  the next command depending on `generate` restored a stale cached
  `src/generated/v1.ts` — deleting 324 lines of endpoints from a file nobody
  edits by hand. CI's stale-client check caught it, which is exactly what that
  check exists for.

## ⚠️ Not done, and said so

- **No Android APK has been built by this pipeline.** There is no Android SDK
  here. `zipalign` and `apksigner` are asserted at the argument level, the same
  footing as the container hardening and the iOS toolchain.
- **No container isolation has actually run.** Docker is unavailable, so
  `DockerBuildSandbox` is asserted by the arguments it would pass.
  `LocalBuildSandbox` refuses to start unless explicitly permitted.
- **No measured cache hit rate.** It needs real builds on a real runner. "70–80%
  of user-triggered builds take the fast path" remains a design assumption from
  the master spec, not an observation.
- **Signing uses the Android debug key**, which is not a secret. Release signing
  means holding customers' upload keys and is Sprint 14 — the code path is
  deliberately not one flag away from it.
- **Artifact storage is a directory.** Same gap as asset blobs, larger stakes:
  an artifact is tens of megabytes and there is no retention policy, so the
  first symptom of the disk filling is builds failing for an unrelated-looking
  reason.
- **The build log has no WebSocket fan-out.** `BuildLogReader` pages the stream
  and returns a resume position, which a client can poll; pushing it is a studio
  concern and arrives with the studio.

Each is in [`ACTION_REQUIRED.md`](ACTION_REQUIRED.md) with what it needs.

## Measurements

See [`docs/perf/baseline-s07.md`](docs/perf/baseline-s07.md). Every figure is a
test with a ceiling, not a number in a document — a recorded figure is one
nobody notices going bad.

| What                      | Budget      | Measured           |
| ------------------------- | ----------- | ------------------ |
| Log redaction, per line   | < 0.5 ms    | **0.017 ms**       |
| Log pipeline throughput   | > 2,000 l/s | **15,457 lines/s** |
| Content patch, 20 MB APK  | < 30 s      | **0.51 s**         |
| Live log page (500 lines) | < 50 ms     | **4.98 ms**        |
