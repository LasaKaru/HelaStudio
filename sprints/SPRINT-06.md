# Sprint 06 — Control Plane API

|                   |                      |
| ----------------- | -------------------- |
| **Weeks**         | 13–14                |
| **Phase**         | 1 — Pipeline         |
| **Capacity**      | 55 h (38 h new work) |
| **Depends on**    | S00, S01             |
| **Blocks**        | S07, S11             |
| **Planned spend** | $0                   |

---

## 1. Sprint goal

Build the multi-tenant control plane: identity, organisations, workspaces, apps, and immutable config versions — with tenant isolation enforced at the database, not just in application code.

⚠️ **Multi-tenancy retrofitted is multi-tenancy broken.** Build it in now, even with one user.

---

## 2. Exit criteria

- [ ] Signup, login, and OAuth (GitHub, Google) working with refresh-token rotation
- [ ] Org → workspace → app → config-version hierarchy with role-based access
- [ ] ⚠️ Postgres **row-level security** enforcing tenant isolation, proven by a test that attempts cross-tenant access at the SQL level
- [ ] Config versions immutable and content-addressed; saving an identical config returns the existing version
- [ ] OpenAPI spec generated, published, and used to generate the TypeScript client
- [ ] Rate limiting on every public endpoint
- [ ] p95 < 100 ms config read, < 400 ms config save+validate under k6 load
- [ ] Coverage ≥ 80% line / 70% branch

---

## 3. Task breakdown

| ID     | Task                                                         | Est.     | Priority |
| ------ | ------------------------------------------------------------ | -------- | -------- |
| T-06.1 | Data model, migrations, and row-level security               | 8 h      | P0       |
| T-06.2 | Identity and authentication                                  | 8 h      | P0       |
| T-06.3 | Authorisation model                                          | 5 h      | P0       |
| T-06.4 | Apps and config-version API                                  | 8 h      | P0       |
| T-06.5 | Cross-cutting: errors, rate limiting, observability, OpenAPI | 6 h      | P0       |
| T-06.6 | Load testing and performance tuning                          | 3 h      | P1       |
|        | **Total**                                                    | **38 h** |          |

---

## 4. Task detail

### T-06.1 — Data model, migrations, and RLS (8 h)

**Schema (core tables):**

```sql
orgs(id, name, slug, plan, created_at, deleted_at)
users(id, email, email_verified_at, created_at)
org_members(org_id, user_id, role, created_at)          -- owner|admin|developer|viewer
workspaces(id, org_id, name, slug)
apps(id, workspace_id, name, bundle_id, current_config_version_id, created_at, archived_at)
config_versions(
    id, app_id, schema_version, body jsonb NOT NULL,
    code_key text, asset_key text, content_key text,
    created_by, created_at, message text
)                                                       -- ⚠️ append-only, never UPDATE
assets(id, org_id, sha256, content_type, bytes, width, height, created_at)
audit_events(id, org_id, actor_id, action, subject_type, subject_id, meta jsonb, at)
```

**Indexes:** every FK; `config_versions(app_id, created_at DESC)`; unique `config_versions(app_id, code_key, asset_key, content_key)` ⚠️ — this unique constraint is what makes "saving an unchanged config is a no-op" correct at the database level rather than as an application race.

**⚠️ Row-level security — the isolation guarantee:**

```sql
ALTER TABLE apps ENABLE ROW LEVEL SECURITY;
CREATE POLICY apps_tenant ON apps
  USING (workspace_id IN (
      SELECT w.id FROM workspaces w
      JOIN org_members m ON m.org_id = w.org_id
      WHERE m.user_id = current_setting('app.user_id')::uuid));
```

- The API sets `app.user_id` per connection/transaction via a `DbCommandInterceptor`.
- The application role is **not** the table owner and does **not** have `BYPASSRLS`.
- A separate migration role owns DDL.

This means a missing `WHERE org_id = ...` in application code is a bug, not a breach. Given that you will later hold customers' signing credentials, defence in depth here is proportionate.

**Migrations:** EF Core migrations, ⚠️ **expand → migrate → contract** always. You will deploy while builds are running; a destructive migration will kill an in-flight build.

**Acceptance criteria:** migrations apply and roll back cleanly; RLS blocks cross-tenant reads even with a raw SQL query as the application role.

**Tests:** `TC-S06-API-001` … `TC-S06-API-006`, `TC-S06-SEC-001`, `TC-S06-SEC-002`

---

### T-06.2 — Identity and authentication (8 h)

**Decisions (ADR `0006-authentication.md`):**

| Decision          | Choice                                                                                                     | Rationale                                                                                                                             |
| ----------------- | ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| Build vs buy      | **Build**, using ASP.NET Core Identity                                                                     | Auth0/Clerk free tiers are generous but this is core, long-lived, and cheap to own. Revisit only if SSO/SAML (S26) proves burdensome. |
| Session mechanism | Short-lived JWT access (15 min) + rotating refresh token in an `HttpOnly`, `Secure`, `SameSite=Lax` cookie | Cookie refresh avoids XSS token theft; short access tokens limit blast radius                                                         |
| Password hashing  | Argon2id                                                                                                   | Current best practice                                                                                                                 |
| OAuth providers   | GitHub, Google                                                                                             | Your audience lives on GitHub                                                                                                         |

**Implementation:**

- Refresh-token **rotation with reuse detection**: each refresh issues a new token and invalidates the old; ⚠️ presenting an already-used token revokes the entire family and raises an audit event. This is the standard defence against token theft and it is cheap to implement now.
- Email verification and password reset via Resend free tier; ⚠️ single-use, 30-minute, constant-time-compared tokens.
- **API tokens** for CLI/CI (`sw_live_…`): store only a hash, show the secret once, scope per workspace, record last-used.
- Login rate limiting: per-IP and per-account, with exponential backoff.
- ⚠️ **Timing-safe** account lookup on login so response time does not reveal whether an email exists.

**Acceptance criteria:** full auth flow works; a replayed refresh token revokes the family; API tokens authenticate and are scoped.

**Tests:** `TC-S06-API-007` … `TC-S06-API-016`, `TC-S06-SEC-003`, `TC-S06-SEC-004`

---

### T-06.3 — Authorisation model (5 h)

**Roles:** `owner` > `admin` > `developer` > `viewer`.

| Action                           | owner | admin | developer | viewer |
| -------------------------------- | ----- | ----- | --------- | ------ |
| Read app + config                | ✅    | ✅    | ✅        | ✅     |
| Save config version              | ✅    | ✅    | ✅        | ❌     |
| Trigger build                    | ✅    | ✅    | ✅        | ❌     |
| Manage signing credentials (S14) | ✅    | ✅    | ❌        | ❌     |
| Submit to store (S15)            | ✅    | ✅    | ❌        | ❌     |
| Manage members                   | ✅    | ✅    | ❌        | ❌     |
| Billing                          | ✅    | ❌    | ❌        | ❌     |
| Delete org                       | ✅    | ❌    | ❌        | ❌     |

**Implementation:**

- ⚠️ **Resource-based authorisation**, not route-based. Check the actual resource's org against the caller's membership. A route-level `[Authorize(Roles="admin")]` does not stop admin-of-org-A touching org-B.
- One `IAuthorizationHandler` per resource type; a single `RequireAppAccess(appId, minRole)` helper used everywhere.
- ⚠️ Deny by default. A new endpoint without an explicit policy must fail closed — add an integration test that enumerates all mapped endpoints via `EndpointDataSource` and asserts every one carries an authorisation policy. This test catches the "forgot to secure the new endpoint" mistake permanently.

**Acceptance criteria:** the permission matrix above is fully covered by tests; the endpoint-enumeration test passes.

**Tests:** `TC-S06-API-017` … `TC-S06-API-024`, `TC-S06-SEC-005`

---

### T-06.4 — Apps and config-version API (8 h)

**Endpoints:**

```
POST   /v1/orgs                                       create org
GET    /v1/orgs/{org}/workspaces
POST   /v1/orgs/{org}/workspaces
GET    /v1/workspaces/{ws}/apps
POST   /v1/workspaces/{ws}/apps                       {name, bundleId, initialUrl}
GET    /v1/apps/{app}
GET    /v1/apps/{app}/config                          current resolved config + ETag
GET    /v1/apps/{app}/config/versions                 cursor-paginated
GET    /v1/apps/{app}/config/versions/{v}
POST   /v1/apps/{app}/config                          save new version
POST   /v1/apps/{app}/config/validate                 validate without saving
GET    /v1/apps/{app}/config/diff?from=&to=
POST   /v1/assets                                     upload icon/splash
```

**Config save semantics — get these right:**

1. Validate against schema + semantic rules (S01). On error, return 422 with the full diagnostic list; ⚠️ **return all diagnostics, never just the first** — round-tripping one error at a time is a terrible experience.
2. Resolve defaults, canonicalise, compute the three hashes.
3. If an existing version has all three hashes equal, **return it with 200**, not a new version. Idempotent saves keep version history meaningful.
4. Otherwise insert (append-only) and update `apps.current_config_version_id` in the same transaction.
5. Emit an audit event.

**Other requirements:**

- `POST /config/validate` must be **fast and unauthenticated-cheap** — the studio calls it on every debounced keystroke. Target < 50 ms server time; no database write.
- **Asset upload:** validate magic bytes (not just content-type header ⚠️), dimensions, and size cap; hash; store in R2 content-addressed; deduplicate. Return `asset://sha256-…`.
- ⚠️ **SSRF guard** on `initialUrl` site analysis: block private, link-local, and cloud-metadata ranges; DNS-resolve and re-check before connecting (defeats DNS rebinding); hard timeout and response size cap.
- ETag on config reads; `If-None-Match` returns 304.
- Cursor pagination on all list endpoints.
- Idempotency-Key support on all creating POSTs.

**Acceptance criteria:** all endpoints implemented; saving an identical config is a no-op returning the same version id; asset upload deduplicates; SSRF probe against `169.254.169.254` is blocked.

**Tests:** `TC-S06-API-025` … `TC-S06-API-042`, `TC-S06-SEC-006`, `TC-S06-SEC-007`

---

### T-06.5 — Cross-cutting concerns (6 h)

1. **Errors:** RFC 7807 `application/problem+json` with a stable `type` URI per error code, mapped from the S01 diagnostic codes so the same code means the same thing everywhere.
2. **Rate limiting:** built-in `AddRateLimiter` — fixed window on reads, token bucket on writes, tighter on auth endpoints, and a per-org concurrency limiter on anything that will trigger builds in S07.
3. **Observability:** OpenTelemetry traces and metrics; `correlationId` middleware propagating into logs; Serilog structured JSON; the four golden signals exposed. ⚠️ Add this now — retrofitting tracing across an async workflow system later is painful.
4. **OpenAPI:** generated from minimal-API metadata, published at `/openapi/v1.json`; CI step generates the TypeScript client into `packages/api-client` and fails if the committed client is stale.
5. **Health endpoints:** `/health/live` (process up) and `/health/ready` (DB + Redis reachable), used by Caddy and later by the runner.
6. **Performance settings** from `01_ENGINEERING_STANDARDS.md` §2.8: `System.Text.Json` source generators, `AsNoTracking` on reads, compiled queries on the config-read path, ReadyToRun publish.
7. ⚠️ **N+1 detector:** an EF Core interceptor that logs a warning above 20 queries per request, and an integration-test assertion that fails the build if any tested endpoint trips it.

**Acceptance criteria:** OpenAPI client generation is clean; rate limits return 429 with `Retry-After`; traces span API → DB; the N+1 detector fails a deliberately-bad query.

**Tests:** `TC-S06-API-043` … `TC-S06-API-050`

---

### T-06.6 — Load testing and tuning (3 h)

1. k6 scripts: config read, config save+validate, app list.
2. Run against the Oracle host with **PgBouncer in transaction mode** ⚠️ — free-tier Postgres has low connection limits and .NET's pool will exhaust them under load.
3. Tune: connection pool size, response compression (Brotli), output caching on config reads.
4. Record a baseline in `docs/perf/baseline-s06.md`; nightly k6 compares against it.

**Acceptance criteria:** p95 config read < 100 ms, save+validate < 400 ms at 50 virtual users on the free-tier host.

**Tests:** `TC-S06-PRF-001`, `TC-S06-PRF-002`

---

## 5. Test cases (selected detail)

| ID               | Type        | Precondition             | Steps                                                 | Expected                                                                     |
| ---------------- | ----------- | ------------------------ | ----------------------------------------------------- | ---------------------------------------------------------------------------- |
| `TC-S06-SEC-001` | Integration | Two orgs with apps       | As org A's user, raw SQL `SELECT * FROM apps`         | Only org A's rows returned (RLS)                                             |
| `TC-S06-SEC-002` | Integration | Two orgs                 | `GET /v1/apps/{orgB_app}` as org A                    | 404 (not 403 — ⚠️ do not leak existence)                                     |
| `TC-S06-SEC-003` | Integration | Valid refresh token      | Use it twice                                          | Second use fails; token family revoked; audit event raised                   |
| `TC-S06-SEC-005` | Integration | —                        | Enumerate all endpoints via `EndpointDataSource`      | Every endpoint has an authorisation policy or an explicit `[AllowAnonymous]` |
| `TC-S06-SEC-006` | Integration | —                        | Create app with `initialUrl: http://169.254.169.254/` | Rejected; no outbound request made                                           |
| `TC-S06-SEC-007` | Integration | —                        | Upload a `.png` whose bytes are a ZIP                 | Rejected on magic-byte check                                                 |
| `TC-S06-API-030` | Integration | App with config v3       | Save byte-identical config                            | 200 with version id v3; no new row                                           |
| `TC-S06-API-031` | Integration | App exists               | Save config with 4 distinct violations                | 422 listing all 4 diagnostics                                                |
| `TC-S06-API-035` | Integration | Same icon uploaded twice | Upload twice                                          | Same `asset://` URI; one row in `assets`                                     |
| `TC-S06-API-038` | Integration | Config read with ETag    | Re-request with `If-None-Match`                       | 304, empty body                                                              |
| `TC-S06-API-041` | Integration | —                        | Two identical POSTs with the same Idempotency-Key     | One resource created; both return the same body                              |
| `TC-S06-PRF-001` | k6          | Seeded data              | 50 VUs, 60 s, config read                             | p95 < 100 ms, 0 errors                                                       |

---

## 6. Risks

| Risk                                                                | Likelihood | Mitigation                                                                                                                                                            |
| ------------------------------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| RLS misconfiguration silently disabled (e.g. app connects as owner) | Medium     | ⚠️ `TC-S06-SEC-001` runs on every PR and asserts the _negative_ case at SQL level, not through the API                                                                |
| Auth built in-house has a subtle flaw                               | Medium     | Use ASP.NET Core Identity primitives, never hand-rolled crypto. Reuse-detection and rate limiting are the two highest-value additions. Book a security review at S25. |
| Free-tier Postgres connection limits under load                     | **High**   | PgBouncer from day one, not after the first outage                                                                                                                    |
| Scope creep into billing or teams UI                                | Medium     | Billing is S17; the studio is S11. This sprint is API only.                                                                                                           |

---

## 7. Deliverables

- `apps/api` — multi-tenant control plane with RLS-enforced isolation
- Auth with rotation + reuse detection, OAuth, and scoped API tokens
- Immutable content-addressed config versioning
- Published OpenAPI + generated TypeScript client
- k6 baseline in `docs/perf/baseline-s06.md`
- `docs/adr/0006-authentication.md`
- `SPRINT-06_REVIEW.md`
