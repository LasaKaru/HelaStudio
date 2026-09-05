# Performance baseline — Sprint 08

Measured on the development container: 4 vCPU, 16 GB, everything on loopback.
Every figure below is asserted by a test with a ceiling
(`IosPerformanceTests`, `TC-S08-PERF-001`–`004`), because a number recorded in
a document is a number nobody notices going bad.

## Sprint 08 figures

| What                             | Budget  | Measured     | Headroom |
| -------------------------------- | ------- | ------------ | -------- |
| Build planning, per plan         | < 50 µs | **7 µs**     | 7×       |
| Fleet placement, 100 hosts       | < 1 ms  | **0.032 ms** | 31×      |
| IPA verification, 60 MB          | < 2 s   | **0.149 ms** | 13,000×  |
| Artifact upload, 60 MB, loopback | < 30 s  | **3.19 s**   | 9×       |
| Artifact download, 60 MB         | < 30 s  | **0.61 s**   | 49×      |

## What the budgets are for

**Build planning** is not about speed. Seven microseconds is what a pure
function over its arguments costs; a plan that read a file or called a service
would not come close, and would stop being testable without the thing it
reached for. The budget exists to catch that change, not to protect a hot path.

**Fleet placement** runs while a customer waits for a slot. It is a scan over a
list, and the budget catches somebody turning it into a query.

**IPA verification** sits between the toolchain finishing and the customer being
told. It reads the zip central directory rather than decompressing entries,
which is why 60 MB costs a seventh of a millisecond. A version that
decompressed to inspect the same things would be four orders of magnitude
slower and would still find nothing more.

**Artifact transfer** is the one figure that is mostly not about our code. What
it catches is the store buffering an artifact whole rather than streaming it —
on a 60 MB IPA, the difference between a constant footprint and one large-object
-heap allocation per concurrent build.

## ⚠️ What these numbers are not

**No iOS build has run.** There is no Mac on this project. The archive and
export are the expensive part of an iOS build by a wide margin, and their cost
here is unknown rather than estimated. Nothing above should be read as an iOS
build time.

**The transfer figures are loopback, not R2.** They cross a `HttpListener` in
the same process tree. Real R2 adds TLS, the public internet, and Cloudflare's
own latency, and the Oracle Always Free host's uplink will dominate all three.
These are floors: they say the store is not the bottleneck, and nothing about
what a customer will wait.

**The upload figure is five times the download.** That is the SDK computing a
streaming signature and framing the body as `aws-chunked` on the way out, which
the download does not do. It is expected, not a defect, and it is recorded here
so nobody spends an afternoon on it later.

**Nothing here contends.** Every measurement had the machine to itself. The
Oracle host contends two cores between the API, PostgreSQL, Redis and Temporal.
