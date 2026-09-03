# Performance baseline — Sprint 07

Measured 2 September 2026 against the build pipeline at commit `8b3b151`.

| What                      | Budget      | Measured           |
| ------------------------- | ----------- | ------------------ |
| Log redaction, per line   | < 0.5 ms    | **0.017 ms**       |
| Log pipeline throughput   | > 2,000 l/s | **15,457 lines/s** |
| Content patch, 20 MB APK  | < 30 s      | **0.51 s**         |
| Live log page (500 lines) | < 50 ms     | **4.98 ms**        |

Reproduce with:

```
dotnet test tests/Shellwright.Orchestrator.Tests --filter "FullyQualifiedName~BuildPerformanceTests"
```

## These are assertions, not a report

Each row is a test with a ceiling several times the measured value. A figure
recorded once in a document is a figure nobody notices going bad; a budget that
fails CI is one somebody has to look at. The headroom is deliberate — these run
on shared CI runners, and a benchmark that fails when a neighbour is busy gets
disabled within a week.

## What each one is for

**Redaction** runs on every line of every build. At half a millisecond, a
200,000-line Gradle build would spend a minute and a half of billable runner
time inside regular expressions. At 0.017 ms it spends three seconds.

**Pipeline throughput** covers redaction, the ndjson archive write and the
batched Redis stream together. Gradle at its most verbose emits a few thousand
lines a second; below that the pipeline becomes the thing a build waits on, and
the customer pays for the orchestrator's own I/O.

**The content patch** is the number the whole three-way cache key exists to
produce. Half a second to rewrite a 20 MB APK, against a full Android build
measured in minutes.

**A live log page** is read on every poll of every viewer of every running
build, so it multiplies by people watching rather than by builds running.

## ⚠️ What these numbers are not

**The patch figure excludes `zipalign` and `apksigner`.** What is measured is
the part this repository owns: fetching the cached artifact, rewriting the
archive with the new configuration, and dropping the old signature.

⚠️ This paragraph previously justified that by claiming there is no Android SDK
in this environment. That was never checked and was false — `/opt/android-sdk`
has build-tools 35 and 36. Sprint 08 (`TC-S07-BLD-094`–`097`) runs the real
`aapt2`, `zipalign` and `apksigner` and verifies the signature. The figure above
still excludes them because it was taken before those tests existed, not because
the tools are absent; it is a floor for the archive rewriting alone.

**There is no measured full-build time, and therefore no measured cache hit
rate.** Both need a real Android toolchain on a real runner. The patch path's
value is argued from the difference between half a second of archive rewriting
and a compile, not demonstrated end to end. Until a runner exists, "70–80% of
user-triggered builds take the fast path" remains a design assumption from
`SHELLWRIGHT_MASTER_SPEC.md` §13 rather than an observation.

**Nothing here crosses a network.** The API, PostgreSQL, Redis and the tests all
share one container. These are floors: they say the code is not the bottleneck.
The Oracle Always Free host contends two cores between the API, PostgreSQL,
Redis and Temporal, and will not reproduce them.
