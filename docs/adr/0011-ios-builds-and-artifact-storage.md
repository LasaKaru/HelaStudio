# 11. iOS builds and artifact storage

Date: 2026-09-03

## Status

Accepted.

## Context

Sprint 08 had to make the pipeline capable of a second platform and a second
machine. Both turn out to be the same problem twice: the orchestrator had a
shape that assumed one platform, one command, one disk, and each assumption was
load-bearing somewhere it should not have been.

None of this ran on a Mac. There is no macOS host on this project and no Apple
Developer account, so what follows is the seam, the rules, and the commands as
data — with the gap recorded in `ACTION_REQUIRED.md` rather than dressed up.

## Decision 1 — A build is a plan, not a command

`BuildCommands.For` returned one `SandboxCommand`. iOS is four steps —
report the toolchain, generate the Xcode project, archive, export — and each
fails differently.

`BuildPlan` is a list of named steps plus the files the plan needs written
first. `BuildPlanner` produces one from a request; Android's has one step.

**Why data rather than behaviour.** Every flag an iOS build would pass can then
be asserted on a Linux CI runner. That is the only review this code can get
before hardware exists, and "we will find out when we have a Mac" is not a
review.

**Why named steps.** `xcodebuild` exits 65 for everything. Without the step
name, an archive that would not compile and an export that would not sign are
the same log line. The name is written into the build log ahead of the step's
own output.

**Cost accepted.** A plan cannot branch on what an earlier step produced. If a
platform ever needs that, the plan stops being the right shape rather than
growing a conditional.

## Decision 2 — The Xcode version is one value, used twice

`IosBuildOptions.XcodeVersion` selects `DEVELOPER_DIR` **and** supplies the
`xcode` entry in the build cache key. They cannot be configured apart.

A deployment that built with one Xcode and keyed its cache with another would
serve customers binaries produced by a toolchain nobody asked for — and would
do it silently, because both halves would look correct in isolation.

## Decision 3 — Cache keys name the toolchain that compiles them

This corrects a defect rather than choosing between options.

The orchestrator computed every cache key against a single injected
`HashContext` that nothing registered and that named no toolchain at all. ADR
0004 requires the toolchain in `codeKey` precisely so a bump invalidates cached
builds; with an empty toolchain map, a bump to AGP, Kotlin or Xcode changed no
key. Every app would have gone on being served artifacts compiled by the
previous toolchain until something else in its configuration happened to change.

`BuildToolchains` returns the per-platform `ToolchainDescriptor` the generator
renders from, so the key the orchestrator computes is the key the project was
actually produced under.

**The API is deliberately not changed to match.** A config version's hash is a
property of the document and identifies a _save_; it must not vary by platform
or a single save would produce two version identities. A build cache key must
vary by both. They share a function and answer different questions.

## Decision 4 — Verification dispatches by platform, and has no default

One verifier was registered for every build, so an IPA was inspected for
`AndroidManifest.xml` and `classes.dex`. It failed closed, which is the only
reason it would not have released an unverified binary — but every iOS build
would have been rejected with a reason naming Android.

`PlatformArtifactVerifier` routes by platform. A platform with no verifier is
rejected by name. A dispatcher whose unknown case passed would be worse than no
dispatcher: it would report "verified" for something nothing inspected.

## Decision 5 — "No macOS fleet" is not "no runner is free"

`RunnerUnavailable` is retryable, because a full fleet empties. A deployment
with no Apple team configured, or a platform with no plan, does not resolve by
waiting — so `PlatformUnavailable` is a separate, non-retryable failure, raised
at planning time rather than after the archive.

Telling a customer their build is queued for a runner that will never exist
costs them twenty minutes before failing anyway.

## Decision 6 — Artifacts live in object storage, addressed by content

`ObjectStoreArtifactStore` speaks S3, which is what Cloudflare R2 speaks. The
filesystem store remains and is chosen when no `ObjectStorage:ServiceUrl` is
configured.

**They are interchangeable because a reference is `artifact://sha256-<digest>`
rather than a URL.** Moving between backends rewrites nothing, and no stored
row depends on which one produced it.

**Deduplication happens before the upload.** If the digest is already present
the bytes are never sent, which is the common case for a patch build.

**Retention is a bucket lifecycle rule.** The store records the intended window
and does not pretend to enforce it; a delete loop in a build worker is a way to
lose artifacts during an incident. The rule that has to exist on the bucket is
`ACTION_REQUIRED.md` item 20.

**Cost accepted.** A lifecycle rule is configured out of band, so nothing in
this repository can prove retention is in force.

## Decision 7 — Two virtual machines per Mac, and a spare host

`MacFleet` caps placement at two VMs per physical host because Apple's licence
permits two, and holds one host in reserve. Placement packs the fullest eligible
host first, which is what preserves the spare rather than spreading load onto it.

The fleet does no I/O. `IMacHostProvider` supplies hosts and health; the
placement rules must hold whoever supplies the hardware, since the plan commits
to moving from a hosted provider to owned Apple Silicon once volume justifies
the capex.

## Consequences

- An iOS build can be reviewed in full without a Mac, and cannot be run without
  one. `IMacHostProvider` has no implementation, and that is the honest state.
- A toolchain bump now costs a full rebuild for every app on that platform.
  That is the intended price and it was previously not being paid.
- Signing remains platform-owned and development-only. Customer upload keys have
  their own custody rules and their own sprint; no flag here enables them.
