# Control plane API

.NET 10 minimal APIs. Auth, organisations, workspaces, apps, and immutable
config versions. `/v1` from the first commit — breaking changes get `/v2`, never
a silent change.

## Running the tests

The tests need a real PostgreSQL, and the fixture will start one for you:

```
dotnet test tests/Shellwright.Api.Tests
```

If you would rather bring the database up yourself, or want the connection
strings for `psql`:

```
eval "$(bash scripts/dev-postgres.sh)"
bash scripts/dev-postgres.sh --stop
```

## The two database roles

`scripts/dev-postgres.sh` creates two roles, and the distinction between them is
the entire tenant isolation story:

| Role                   | Owns the schema | Used by                      |
| ---------------------- | --------------- | ---------------------------- |
| `shellwright_migrator` | yes             | migrations only              |
| `shellwright_app`      | no              | every request the API serves |

⚠️ A table's owner is **exempt from its own row-level security policies**. Point
the API's connection string at `shellwright_migrator` and every policy in
`Data/Sql/RowLevelSecurity.up.sql` silently becomes decoration, with no outward
symptom until one customer sees another's data.
`RowLevelSecurityTests.Application_role_owns_nothing_and_cannot_bypass_policies`
exists to catch exactly that deployment mistake, and it asserts against the live
connection rather than against configuration.

## Constraints fixed by earlier decisions

- `InvariantGlobalization` must be **false**, because this project consumes
  `Shellwright.ConfigSchema` and its canonicalisation depends on Unicode
  normalisation. See ADR 0003.
- Config versions are append-only. No `UPDATE`, ever — it is what makes cache
  correctness and the audit trail trivially right rather than carefully right.
  This is enforced by the grants, not by convention: `shellwright_app` has
  `SELECT, INSERT` on `config_versions` and nothing more.

## Adding a migration

```
export SHELLWRIGHT_MIGRATION_CONNECTION="$SHELLWRIGHT_TEST_PG_MIGRATOR"
dotnet ef migrations add SomeName --project apps/api/Shellwright.Api --output-dir Data/Migrations
```

Two things to do afterwards:

1. **Strip the byte-order mark.** The EF scaffolder writes UTF-8 with a BOM and
   `dotnet format` rejects it, so CI fails on a file you did not write.
2. **Decide about row-level security.** A new table with no policy is invisible
   to `RowLevelSecurityTests.Every_table_is_policed_or_explicitly_exempt` only
   in the sense that the test will fail until you either add a policy or add the
   table to the exemption list with a reason. That decision belongs in the pull
   request, not in a follow-up.

Hand-written migrations keep their SQL in `Data/Sql/*.sql` so that policies and
grants are reviewed as SQL rather than as a C# string literal.

## Builds

`POST /v1/apps/{appId}/builds` starts one. Six endpoints in total: start, list,
read, cancel, get a download link, and follow that link.

### The Idempotency-Key is required here

Everywhere else in this API the header is optional. On builds it is mandatory,
and a request without one is refused with `API_IDEMPOTENCY_KEY_REQUIRED`.

A retried configuration save costs a duplicate row that the content address
collapses anyway. A retried build costs runner minutes somebody is billed for,
and the server has no other way to tell "start another build" from "I did not
hear you". Refusing the request is friendlier than the alternative, which is a
duplicate charge nobody notices until the invoice.

Idempotence is a unique index on `(app_id, idempotency_key)`, not a
read-then-write: two identical requests racing each other both find nothing on a
read, and only the index stops both from inserting.

### Concurrency is per organisation

`Builds:MaxConcurrentBuildsPerOrg` defaults to two — enough for Android and iOS
together, few enough that a misconfigured pipeline cannot queue a hundred. It is
per organisation rather than global because the failure being prevented is one
customer's CI loop consuming the fleet; a global cap would allow exactly that
and only tell everybody else the service is slow.

### Cancelling does not write "cancelled"

`POST .../cancel` asks Temporal to stop the workflow and returns the build
unchanged. The transition to `Cancelled` is recorded by the activity that runs
when the workflow actually stops. Writing it optimistically would let the row
say "stopped" while a runner kept burning metered minutes.

Cancelling a build that has already finished is a `409`, not a silent `200` — it
is nearly always a mistake about which build is which, and answering "fine"
leaves the caller believing they stopped something.

### Artifact downloads are signed links, not authenticated requests

`GET .../artifact` returns a URL valid for fifteen minutes. The signature covers
the build, the artifact reference and the expiry together, so a link issued for
one build cannot be replayed against another that produced identical bytes —
which, with a content-addressed store, is exactly what a cache hit produces.

The download endpoint itself is anonymous. That is deliberate: an artifact is
fetched by a browser, a `curl` in somebody's CI, or an emulator, none of which
reliably carries a bearer token and all of which log the URL. A signed link
naming one build and dying in fifteen minutes is a narrower grant than an access
token that opens the whole API.

Being anonymous, it has no identity to stamp, so row-level security hides every
row from it. Rather than give the API a policy-bypassing connection — which
every other handler in the process could then reach — there is one
`SECURITY DEFINER` function, `app_artifact_for_download`, that answers exactly
that question and returns three columns. `Data/Sql/ArtifactDownload.up.sql` sets
out why the alternatives were rejected.
