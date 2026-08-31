# Control plane API

Empty until **Sprint 06**.

.NET 10 minimal APIs. Auth, organisations, workspaces, apps, and immutable
config versions. `/v1` from the first commit — breaking changes get `/v2`, never
a silent change.

Two constraints already fixed by earlier decisions:

- `InvariantGlobalization` must be **false**, because this project consumes
  `Shellwright.ConfigSchema` and its canonicalisation depends on Unicode
  normalisation. See ADR 0003.
- Config versions are append-only. No `UPDATE`, ever — it is what makes cache
  correctness and the audit trail trivially right rather than carefully right.
