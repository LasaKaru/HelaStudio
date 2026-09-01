# 8. Generate `project.yml`, not `project.pbxproj`

Date: 2026-09-01

## Status

Accepted

⚠️ `sprints/SPRINT-05.md` refers to this decision as ADR 0005; that number was
taken by the ShellCore/ShellApp split before Sprint 05 began.

## Context

Android's project is a handful of readable text files. iOS's is
`project.pbxproj`: an ordered property list whose objects are keyed by 96-bit
hexadecimal UUIDs, with no published format specification and a layout that
shifts between Xcode releases.

The generator has to produce one, deterministically, for every customer.

| Option                                         | Verdict                                                                         |
| ---------------------------------------------- | ------------------------------------------------------------------------------- |
| Template the raw `.pbxproj`                    | ❌ Unreviewable, and breaks whenever Xcode's format moves                       |
| Generate it with **XcodeGen** from a YAML spec | ✅ Chosen                                                                       |
| Generate it with **Tuist**                     | Viable; a Swift-based build system is far more capability than a template needs |
| SwiftPM alone                                  | ❌ Cannot express app targets, entitlements, or extensions                      |

Templating the plist directly fails the test that matters most for this
codebase: a reviewer cannot read a diff of it and say whether it is right. A
3,000-line generated plist with UUID keys is a file nobody will ever review
again, which makes the golden corpus worthless for the one platform where
mistakes are most expensive.

## Decision

**Template a 60-line `project.yml` and let XcodeGen build the project.**

The generator emits YAML it can be held to, and the `.pbxproj` is produced on
the Mac at build time by a tool whose job that is. `build.sh` ships in every
generated project and runs `xcodegen generate` before `xcodebuild`, so a
customer with only Xcode installed can build what they were paying for — the
beginning of source export (BD-10).

Two consequences follow directly.

**XcodeGen and Xcode are both in the toolchain descriptor.** XcodeGen derives
its UUIDs from paths and names, which makes it deterministic in principle, but a
version bump can change that derivation. Xcode's project format shifts roughly
annually, and that break is certain rather than hypothetical. Both decide the
bytes of the result, so both belong in the cache key — a version bump must
invalidate builds deliberately rather than surface as a mysterious full rebuild.

**The golden corpus snapshots `project.yml`, not the project.** The input is
what this generator owns and what a reviewer can actually read. Whether XcodeGen
turns it into the same bytes twice is a question about XcodeGen, answered on a
Mac by the double-generation check, not something to assert from a Linux CI job
that cannot run it.

## Consequences

Every generated iOS project depends on XcodeGen being installed. That is one
`brew install` on a build machine, and it is stated in `build.sh` with a check
that fails with a usable message rather than a missing-command error.

⚠️ **The verification gap is real and worth naming.** Nothing in the Linux test
suite runs `xcodebuild`, `xcodegen` or `plutil`. The tests assert that the spec
is well-formed YAML, that every plist parses, and that the right keys are
present for a given config — genuine checks, but not the same as "Xcode accepts
this". Sprint 04 learned this the hard way on Android: the `namespace` bug
passed 71 unit tests and every golden file, because a snapshot records what the
generator produced and not whether the toolchain accepts it. On iOS that gap is
wider, because the toolchain is further away.

The gap closes in two places, and only there: the Codemagic `ios-verify`
workflow, which builds unsigned on a Mac and needs no Apple account, and the
nightly macOS job once `shells/ios` becomes a public repository and its macOS
minutes stop being metered (ADR 0002).

Until one of those runs, iOS generation should be treated as **unproven against
a real toolchain**, however green the test suite is.
