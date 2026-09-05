# Sprint 06 review — control plane API

**Goal:** build the multi-tenant control plane — identity, organisations,
workspaces, apps, and immutable config versions — with tenant isolation enforced
at the database rather than only in application code.

## Exit criteria

| Criterion                                                                  | Status                                                    |
| -------------------------------------------------------------------------- | --------------------------------------------------------- |
| Signup, login, refresh-token rotation                                      | ✅ with reuse detection revoking the family               |
| OAuth (GitHub, Google)                                                     | ⚠️ **wired, not proven end to end** — no live credentials |
| Org → workspace → app → config-version hierarchy with roles                | ✅                                                        |
| **Postgres RLS proven by cross-tenant access at the SQL level**            | ✅ 16 tests as the application role, not through the API  |
| Config versions immutable and content-addressed; identical save is a no-op | ✅ enforced by a unique index, not a read-then-write      |
| OpenAPI generated, published, TypeScript client generated from it          | ✅ 24 paths, 28 schemas, CI fails when stale              |
| Rate limiting on every public endpoint                                     | ✅ three policies, `Retry-After` on every 429             |
| p95 < 100 ms config read, < 400 ms save+validate under k6                  | ✅ **12.1 ms** and **20.4 ms** — but see the caveat below |
| Coverage ≥ 80% line / 70% branch                                           | ⚠️ **not measured** — no gate exists yet                  |

**515 .NET tests** (192 new) and **241 TypeScript tests**, all green.

## What shipped

### Tenant isolation is a database guarantee

Two roles, and the distinction between them is the whole thing. `shellwright_migrator`
owns the schema; `shellwright_app` owns nothing, holds no `BYPASSRLS`, and is
what every request connects as.

⚠️ A table's owner is exempt from its own policies. A deployment that collapses
the two roles passes every functional test and has no isolation at all. Granting
`BYPASSRLS` to the application role fails **nine** of the security tests, which
is the evidence that they are testing the property and not their own fixtures.

Policies go through `SECURITY DEFINER` membership functions — not for speed, but
because `org_members` carries a policy defined in terms of membership and a
policy that queries its own table recurses forever.

### Append-only is a grant

`config_versions` and `audit_events` are granted `SELECT, INSERT` and nothing
more, so a future handler that tries to rewrite history fails loudly rather than
quietly invalidating every build cached against it. `security_events` goes
further — `INSERT` and no `SELECT` — because the component most likely to be
compromised should not be able to edit the record of the compromise.

### Rotation that actually detects theft

Keeping spent refresh tokens and revoking the family on replay. Both parties get
signed out, which is the point: the user notices and signs in again, and the
stolen token is worthless. Every failure reads "session expired" from outside,
because telling an attacker that the replay is what gave them away only tells
them to move faster. [ADR 0009](docs/adr/0009-authentication.md).

### Two tests that make forgetting impossible

`Every_endpoint_declares_an_authorisation_decision` enumerates the route table
and fails on any endpoint that has not written its decision down. A companion
pins the exact list of anonymous endpoints, so opening one to the internet needs
approval in two places. Both were probed by adding an undecorated endpoint and
watching them fail.

`Every_table_is_policed_or_explicitly_exempt` does the same for the schema. It
caught three credential tables during the sprint and forced a documented
exemption for each.

### The client cannot drift from the server

The OpenAPI document is generated from the route table, the TypeScript client
from the document, both committed, and CI fails when either is stale. That puts
an endpoint change and the client's view of it in one diff.

## What the tests caught before a human would have

| Defect                                                                    | What would have happened                                          |
| ------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| The API-token resolver selected snake_case columns into a CLR projection  | Every request presenting a token throws — 100% failure            |
| JWT lifetimes validated against `DateTime.UtcNow`, not the injected clock | Issuer and validator disagree; expiry untestable without sleeping |
| The entity tag was computed and compared but never sent                   | Conditional requests silently impossible for every client         |
| Roles emitted as names and rejected on the way back in                    | Every role change returns a bare 400                              |
| A `--` inside an XML comment                                              | NU1010 against every package, reading as though CPM was off       |
| `Microsoft.OpenApi` pulled a high-severity advisory transitively          | Security job fails the build, correctly                           |

And one the _shared contract_ was missing: a NUL in any config string passes the
schema, passes every rule, and then cannot be stored — PostgreSQL's `jsonb` has
no representation for U+0000. Fixed in both engines as `CFG_CONTROL_CHARACTER`
rather than at the API boundary, so the studio says so while somebody is typing.

## What the load test taught

The first run of `config-read.js`, written the way the sprint plan describes it,
failed **99.95% of 840,000 requests** and reported a p95 for the 374 that got
through. Two things were wrong and both were mine:

1. **`constant-vus` with no think time is a saturation test.** Each user issues
   its next request the instant the last returns, so the offered load is whatever
   the server allows — 13,418 requests a second, which no studio produces. The
   scripts now use `constant-arrival-rate`, so the percentile answers _what is
   the latency at 200 requests a second_ and `dropped_iterations` says outright
   when the server could not keep up.
2. **The rate limiter was the first thing the load met.** Limits are now
   configurable and the harness raises them, which is stated in the baseline
   rather than hidden. Whether the production limits are right is a separate
   question, answered by an integration test.

Numbers in [docs/perf/baseline-s06.md](docs/perf/baseline-s06.md).

## Unmet, and honestly so

### OAuth is unproven end to end

Account linking is tested directly — matching on the provider's stable id first
and the address only when no link exists, which is the order that stops an
address change handing over an account. But no test completes a real
authorisation code exchange, because that needs live credentials at both
providers. `ACTION_REQUIRED.md` item 14.

### Blob storage is a directory

R2 is the plan and needs credentials this project does not have. The seam is one
class and the gap is recorded rather than hidden behind an interface name that
implies otherwise.

### Rate limiting is per instance

In-process, so three instances mean three times the limit. Acceptable for
protecting a host from a runaway client; unacceptable for anything a customer is
billed against. Needs a shared store before the second instance exists.

### No coverage gate

The sprint asks for ≥ 80% line and ≥ 70% branch. Coverage is collected in CI and
uploaded; nothing fails on it. Carried to Sprint 07, together with the same gap
noted for the codegen package in Sprint 05.

### PgBouncer is not in the picture

The sprint plan flags free-tier connection limits as its one **high**-likelihood
risk, and the baseline ran a 40-connection pool straight at the server. Nothing
measured says what happens behind a pooler in transaction mode. It belongs with
the infrastructure it protects, which does not exist yet.

## Carried into Sprint 07

1. Verify OAuth against real provider credentials.
2. Replace the filesystem blob store with R2.
3. Move rate limiting to a shared store, or accept per-instance limits in writing.
4. A coverage gate, covering the codegen package as well.
5. PgBouncer, measured, once there is a host.
6. Compiled queries on the config-read path — the baseline is comfortable enough
   that this was not worth doing blind.
