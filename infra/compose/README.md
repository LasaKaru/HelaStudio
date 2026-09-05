# Compose stack

The Oracle Always Free host runs Postgres, Redis, Temporal, and Caddy here.
Written during T-00.5 — see `docs/ops/provisioning.md`.

⚠️ **Every service needs an explicit memory limit.** The host is 2 OCPU / 12 GB,
and an unbounded Gradle daemon sitting next to Postgres will take the whole box
down. Verify with `docker stats`, not by intuition.

⚠️ Every image must have an arm64 build. Record what you verified in
`docs/ops/arm64-compat.md`.
