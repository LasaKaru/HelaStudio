# 1. Record architecture decisions

Date: 2026-08-31

## Status

Accepted

## Context

This programme runs for roughly twelve months with one engineer. Decisions made
in month one will be questioned in month eight by someone who has forgotten the
constraints that produced them — and that someone is the same person.

Several decisions here are one-way doors: the configuration schema shape, the
bridge protocol, the plugin manifest format, and the artifact hashing scheme.
Each is depended upon by generated projects, stored customer configurations, and
published binaries. Reversing one is not a refactor; it is a migration campaign.

## Decision

Architecture decisions are recorded as files in `docs/adr/`, numbered
sequentially, using Michael Nygard's template: Context, Decision, Consequences.

`01_ENGINEERING_STANDARDS.md` §1.4 requires an ADR before implementing any
one-way door. An ADR is written _before_ the code, not as a write-up afterwards.

A superseded ADR is not deleted or edited. Its status changes to `Superseded by
NNNN`, and the new ADR records why the original reasoning stopped holding. The
record of a decision that turned out wrong is more useful than its absence.

## Consequences

- Every one-way door has a written rationale a stranger can reconstruct.
- Writing the ADR first surfaces the decision as a decision, rather than letting
  it accrete out of whatever the first implementation happened to do.
- The overhead is real but small: roughly thirty minutes per genuinely
  significant decision. Decisions that do not warrant thirty minutes do not
  warrant an ADR.
