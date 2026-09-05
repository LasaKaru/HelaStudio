# 10. Build orchestration

Date: 2026-09-02

## Status

Accepted.

## Context

A build takes minutes, runs untrusted configuration through a real toolchain,
costs metered runner time, and has to be cancellable, resumable and billable.
Four decisions in Sprint 07 constrain each other enough to be worth recording
together.

## Decision 1 — Temporal, not a queue

Retries, cancellation, heartbeats and compensation are the reasons. A queue
gives message delivery; everything above would then be written by hand, and the
part written by hand is always the part that half-works: a build that a worker
died in the middle of, a cancellation that arrives while the compiler is
running, a compensating cleanup that must itself survive a crash.

**Cost accepted.** Temporal is a server to run, and workflow code must be
deterministic — no clocks, no randomness, no I/O — which is a real constraint on
how the build is expressed. Activity code has none of those limits, so
everything that touches the world lives there.

**Postgres is still the record.** Workflow history is archived, cannot be joined
to a tenant, and cannot be paginated. "How is my build going" is answered from a
table that activities write.

## Decision 2 — The cache key is split three ways, and the names are held to it

`codeKey`, `assetKey` and `contentKey` partition the configuration by what a
change to it actually costs. The outcomes are `Miss`, `Warm`, `Patch` and
`Complete`.

**`Warm` is a full toolchain run** and exists as a separate value only so
metering can tell a warm dependency cache from a cold one. Anything in the asset
key — an icon, a colour, a tab label — is a _compiled_ Android resource, so
replacing one means recompiling `resources.arsc` and relinking.

**`Patch` is the case the split exists for.** The start URL, allowed origins,
navigation, link rules and version string are read at run time from one
uncompiled JSON file in the APK. Replacing that entry and re-signing takes
half a second where a compile takes minutes.

⚠️ **The first version of this code claimed a patch and ran a full build.** It
reported `WasPatched: true` for a four-minute compile. Metering, queue estimates
and the customer's bill are all computed from that flag, so an outcome that
names a cost nobody paid is worse than having no cache at all. The names are now
held to what the code does, and a patch that turns out to be impossible falls
through to a full build and reports itself as one.

## Decision 3 — The orchestrator gets its own database role, not `BYPASSRLS`

The orchestrator acts for no particular user — it runs builds for everybody — so
it cannot be scoped by membership the way the API is.

**Rejected: `BYPASSRLS`.** A role that bypasses row-level security is unbounded
by construction. It could read every user, every organisation, every API token
hash and every refresh token, and nothing in the schema would record that this
was ever a decision.

**Rejected: reusing the API's role with an impersonated identity.** A build
requested by someone later removed from the organisation must still finish and
still be billed.

**Accepted: `shellwright_runner`,** with total reach over tenants and a
six-table grant list. Its policies are scoped `TO shellwright_runner`, which is
load-bearing: permissive policies are OR'd, so one `USING (true)` without that
clause would hand the API's role every tenant's rows, with every other test
still passing. There is a test that fails by name if the clause is removed.

**Consequence.** A compromised orchestrator can read every tenant's builds and
artifacts — which it must, since it runs them — and cannot read a user, a
membership, an asset, an audit event or any credential table at all. That is a
legible blast radius rather than "everything".

## Decision 4 — Artifacts are fetched with signed links, not access tokens

An artifact is downloaded by a browser, a `curl` in somebody's CI, or an
emulator. None reliably carries a bearer token; all of them log the URL.

**Accepted:** a 15-minute HMAC link naming one build and one artifact, served
from an anonymous endpoint. That is a narrower grant than an access token which
opens the whole API for an hour.

⚠️ The signature covers the build, the artifact reference and the expiry
together. Over the artifact alone, a link issued for one build could be replayed
against another that produced identical bytes — which, with a content-addressed
store, is exactly what a cache hit produces.

**Consequence.** The endpoint has no identity to stamp, so every policy hides
every row from it. Rather than give the API a policy-bypassing connection that
every other handler could reach, one `SECURITY DEFINER` function answers exactly
that question and returns three columns.

## Consequences

- Two services now write to the same database as different roles, and the build
  state enum is declared in both. `BuildContractTests` holds them equal in name
  and number; without it a divergence does not fail, it reinterprets.
- Release signing is deliberately not one flag away. Sprint 07 signs with the
  Android debug key, which is not a secret. Holding customers' upload keys needs
  the custody design in §18.2 and is Sprint 14.
- Container isolation and the signing tools are asserted at the argument level
  only, because this environment has neither Docker nor an Android SDK. Both
  gaps are in `ACTION_REQUIRED.md` rather than hidden.
