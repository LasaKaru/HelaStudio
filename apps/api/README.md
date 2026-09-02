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
