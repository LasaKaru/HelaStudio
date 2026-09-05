# Sprint 08 review — iOS builds, the Mac fleet, and artifact storage

**Goal:** make the pipeline capable of a second platform and a second machine —
an iOS build that produces a verified IPA, a macOS fleet that respects Apple's
licence, and artifact storage that more than one runner can share.

## Exit criteria

| Criterion                                            | Status                                                                   |
| ---------------------------------------------------- | ------------------------------------------------------------------------ |
| iOS build commands, with Xcode pinned per deployment | ✅ every flag, path and environment variable asserted                    |
| **An IPA produced end to end**                       | ❌ **not done** — there is no Mac, and no attempt was made to fake one   |
| IPA verification before release                      | ✅ seven rejection cases, against real archives                          |
| macOS fleet: two VMs per host, N+1 reserve           | ✅ 13 tests; raising the cap to 4 fails three of them                    |
| Provider abstraction for hosted → owned migration    | ⚠️ **the seam only** — `IMacHostProvider` has no implementation          |
| Object storage for artifacts, content-addressed      | ✅ against a real S3 endpoint, streaming, 60 MB round trip               |
| Retention so artifacts stop accumulating             | ⚠️ **a bucket lifecycle rule** — configured out of band, unprovable here |
| Android toolchain proven against the real SDK        | ✅ `aapt2` builds an APK, `apksigner verify` accepts our signature       |
| Toolchain version in the build cache key             | ✅ **and it was not there at all before this sprint** — see below        |
| Measured costs for the new paths                     | ✅ four budgets, all asserted                                            |

**806 .NET tests** (86 new, all orchestrator) and **241 TypeScript tests**, all
green. Programme total **1,220**.

## The defect this sprint existed to find

**No toolchain version was in the orchestrator's build cache key. At all.**

The orchestrator computed every key against an injected `HashContext` that
nothing registered, whose toolchain map was empty. ADR 0004 requires the
toolchain in `codeKey` for one reason: a bump to AGP, Kotlin or Xcode must
invalidate every cached build. With an empty map it invalidated nothing.

The consequence, had this shipped: every app would have gone on being served
artifacts compiled by the _previous_ toolchain, indefinitely, until something
else in its configuration happened to change. No error, no log line, and every
test green — because nothing tested it.

It is fixed by `BuildToolchains`, which returns the same per-platform
`ToolchainDescriptor` the generator renders from, so the key the orchestrator
computes is the key the project was actually built under.
`TC-S08-BLD-050`–`051` are the regressions: bumping Xcode must change every iOS
code key and must leave Android's alone.

⚠️ **The API is deliberately not changed to match.** A config version's hash
identifies a _save_ and must not vary by platform, or one save would produce two
version identities. A build cache key must vary by both. Same function,
different questions.

## What shipped

### A build is a plan, not a command

`BuildCommands.For` returned one `SandboxCommand`. iOS is four steps — report
the toolchain, generate the Xcode project, archive, export — and each fails
differently. `BuildPlan` is now a list of named steps plus the files to write
first; Android's has one step.

Being data rather than behaviour is what makes an iOS build reviewable on Linux.
Every flag can be asserted without a Mac, which is the only review this code was
going to get.

The step names are not cosmetic: `xcodebuild` exits 65 for everything, so
without them an archive that would not compile and an export that would not sign
are the same log line.

### Verification dispatches by platform

Before this sprint one verifier was registered for every build, so an IPA would
have been inspected for `AndroidManifest.xml` and `classes.dex`. It failed
closed — which is the only reason it would not have released something
unverified — but every iOS build would have been rejected with a reason naming
Android, and nobody reading it could have told a broken build from a broken
check.

`PlatformArtifactVerifier` routes by platform and has no default arm: a platform
with no verifier is rejected by name.

### "No macOS fleet" is not "no runner is free"

`RunnerUnavailable` is retryable because a full fleet empties. A deployment with
no Apple team configured does not resolve by waiting, so `PlatformUnavailable`
is a separate non-retryable failure raised at planning time — before the lease,
and long before the archive. Telling a customer their build is queued for a
runner that will never exist costs them twenty minutes before failing anyway.

### Artifacts live in object storage

S3, which is what R2 speaks. Content addressing is unchanged, so the filesystem
store and the object store are interchangeable and no stored row depends on
which produced it. Deduplication happens before the upload, which is the common
case for a patch build. Retention is a bucket lifecycle rule rather than a delete
loop in a build worker.

The tests run against a real `HttpListener` speaking S3, not a mocked SDK
client — which is how they caught the SDK framing uploads as `aws-chunked`, a
detail no mock would have had an opinion about.

### The Mac fleet's rules hold whoever supplies the hardware

Two VMs per physical host because Apple's licence permits two; one host held in
reserve; placement packs the fullest eligible host first, which is what
_preserves_ the spare rather than spreading onto it. `MacFleet` does no I/O, so
the rules are testable and cannot be quietly renegotiated by a provider.

## Defects the tests caught before a human would have

- **Metering measured the wrong thing.** My first multi-step `BuildAsync` billed
  wall-clock time across the plan, which includes the orchestrator's own file
  writes and scheduling. It now sums what the sandbox measured for each command:
  the customer pays for the toolchain running, not for us.
- **The export options plist and the export command disagreed.** Two literals
  for one path. `TC-S08-BLD-035` asserts the export reads the plist the plan
  writes — a failure that would otherwise have appeared only _after_ an archive,
  which is after all of the cost.
- **`ExportOptions` interpolated a team id into XML unchecked.** The plist
  decides how a binary is signed. It now requires Apple's ten-character
  identifier, at the point that writes the XML rather than only where the value
  is configured.
- **The scheme name was a guess.** `TC-S08-BLD-042` reads the target out of
  `shells/ios/templates/project.yml.tmpl` and asserts the constant matches, so a
  rename in the shell cannot silently become "scheme not found" at the archive.
- **`zipalign` and `apksigner` were bare names off `PATH`**, which made BD-09's
  toolchain pinning unenforceable for the two tools that matter most. Now
  resolved through `AndroidToolchain`.

Each of these was proved non-vacuous by deliberately reintroducing the fault and
watching the named test fail. Nine of the twenty-nine planner and toolchain
tests fail under five reverted invariants; five of the ten activity tests fail
under five more.

## Corrections to earlier sprints

- **Sprint 07's `SandboxHardeningTests` asserted a stopgap.** A test named
  `An_ios_build_is_refused_until_there_is_a_mac` checked for the message "iOS
  builds need a macOS runner, which arrives in Sprint 08". Sprint 08 arrived.
  What survives is the narrower true property: `BuildCommands` is the Android
  toolchain and refuses anything else, so a routing mistake fails there rather
  than running `./gradlew` in a directory with no Gradle in it.
- **`docs/perf/baseline-s07.md` still claimed there is no Android SDK here.**
  There is, and Sprint 08 used it. The paragraph now says what the figure
  actually excludes and why.
- **The nightly "Full suite" job had no Redis.** It deliberately runs without
  service containers so the setup scripts' own paths are exercised, but
  `scripts/dev-redis.sh` starts a server and will not install one — and the
  runner image ships PostgreSQL and not Redis. Fourteen log-pipeline tests
  failed there from the day they were written. The job now installs Redis first.

## ⚠️ Not done, and said so

**No iOS build has ever run.** Not a partial one, not a simulated one. There is
no macOS host on this project and no Apple Developer account. What exists is the
seam (`IMacHostProvider`, with no implementation), the placement rules, the
commands as data, and the verifier. `ACTION_REQUIRED.md` items 22 and 23 are
what would change that.

The distinction that matters: the Linux tests establish that every flag, path,
environment variable and step order is what it should be, and that an IPA is
rejected for each of seven ways it can be unusable. They establish nothing about
whether `xcodebuild` accepts any of it.

**BD-09 is partial.** The Xcode version is pinned and is a single source for
both the toolchain selection and the cache key. It is not pinned _per app_ — it
is a deployment setting, so every app on a deployment builds with the same
toolchain. Per-app pinning needs a column on the app and a fleet that can
satisfy two versions at once.

**Retention is unproven.** The store records the intended 90-day window and
refuses to pretend it enforces it. A lifecycle rule is configured on the bucket,
out of band, and nothing in this repository can observe whether it exists.

**Release signing is still platform-owned and development-only.** Customer
upload keys have their own custody rules and their own sprint. No configuration
value here turns that on, deliberately.

**The container hardening gap from Sprint 07 is unchanged.** There is still no
container runtime here, so the Docker flags are asserted at the argument level
and not observed being honoured.

## Measurements

See [`docs/perf/baseline-s08.md`](docs/perf/baseline-s08.md). Every figure is a
test with a ceiling.

| What                             | Budget  | Measured     |
| -------------------------------- | ------- | ------------ |
| Build planning, per plan         | < 50 µs | **7 µs**     |
| Fleet placement, 100 hosts       | < 1 ms  | **0.032 ms** |
| IPA verification, 60 MB          | < 2 s   | **0.149 ms** |
| Artifact upload, 60 MB, loopback | < 30 s  | **3.19 s**   |
| Artifact download, 60 MB         | < 30 s  | **0.61 s**   |

⚠️ None of these is an iOS build time. The archive and export are the expensive
part by a wide margin and their cost is unknown here rather than estimated.
