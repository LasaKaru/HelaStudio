# 9. Authentication and tenant isolation

Date: 2026-09-02

## Status

Accepted.

⚠️ The sprint plan calls this ADR `0006-authentication.md`. It is 0009 because
0006 through 0008 were taken during Sprints 04 and 05, before this one was
written. The number is an ordering, not a name.

## Context

The control plane holds, or will hold, three things worth attacking: customers'
application configurations, their uploaded assets, and — from Sprint 14 — their
signing credentials. The last of those makes a breach company-ending rather than
embarrassing, and the master spec says so in §1.4.

Everything below was decided in one sitting because the decisions constrain each
other. Splitting them across four ADRs would hide that.

## Decision

### Build authentication rather than buy it

ASP.NET Core Identity's primitives, not Auth0 or Clerk.

Both have free tiers generous enough for a long time. The reason not to take one
is that authentication here is long-lived, core, and cheap to own: the whole of
it is four services and a table. The reason it might be wrong is SSO and SAML in
Sprint 26, which is genuinely tedious to build; that is the point at which to
revisit, and not before.

### Argon2id, at the memory-constrained profile

RFC 9106's second recommended option — 64 MiB, three passes, four lanes —
rather than the first.

⚠️ Deliberately the smaller profile. The control plane runs on a 12 GB host
shared with PostgreSQL, Redis, and build containers. At 2 GiB of working memory
per attempt, a login storm is a denial of service we would have built ourselves.

Parameters are encoded in each hash rather than read from configuration at
verification time, so raising them later is a gradual rehash-on-login rather
than a mass password reset.

### Short JWT access tokens, rotating refresh token in a cookie

Fifteen minutes and thirty days.

The asymmetry is the design. Script running in the studio's origin can read
anything the studio can read, so a refresh token in local storage is a
thirty-day session an XSS bug hands over. `HttpOnly` puts it where script cannot
reach; the fifteen-minute access token is what script holds instead, and it
expires on its own.

`SameSite=Lax` rather than `Strict`, because the OAuth callback is a cross-site
top-level navigation back into the API and Strict would drop the cookie on
exactly that request.

### Rotation with reuse detection

⚠️ Rotation alone does not detect theft. An attacker who copies a refresh token
and uses it before the legitimate client simply becomes the client, and the real
user's next refresh fails in a way indistinguishable from an ordinary expiry.

So spent links are kept, and a second presentation revokes the entire family.
Both parties are signed out, which is the desired outcome: the user notices,
signs in again, and the stolen token is worthless. The response says only
"session expired" — telling an attacker that the replay is what gave them away
only tells them to move faster next time.

### Identity is global; tenancy is not

An account belongs to many organisations. Tenant isolation therefore applies to
organisation-scoped tables and not to `users`, `refresh_tokens`, `user_tokens`,
or `oauth_identities` — there is no tenant to isolate those to, and a membership
predicate over them would be meaningless rather than strict.

`RowLevelSecurityTests.Every_table_is_policed_or_explicitly_exempt` enumerates
every table and fails on any that is neither policed nor on a named exemption
list, so this stays a decision rather than becoming an omission.

### Row-level security, with two database roles

`shellwright_migrator` owns the schema and runs migrations. `shellwright_app`
owns nothing, holds no `BYPASSRLS`, and is what every request connects as.

⚠️ A table's owner is exempt from its own policies. Collapsing the two roles
produces a deployment that behaves identically in every functional test and has
no isolation whatsoever. Three assertions cover it, against the live connection
rather than against configuration.

Policies are expressed through `SECURITY DEFINER` membership functions, not as
an optimisation but because `org_members` carries a policy defined in terms of
membership, and a policy that queries its own table recurses forever.

### Append-only is a grant, not a convention

`shellwright_app` has `SELECT, INSERT` on `config_versions` and `audit_events`,
and nothing more. A future handler that tries to "just fix" a stored config
fails immediately rather than quietly invalidating every build cached against
it.

`security_events` goes further: `INSERT` and no `SELECT`. The component most
likely to be compromised should not be able to read, alter, or erase the record
of the compromise.

### API tokens narrow, and can never widen

A token's effective role is the lesser of its own ceiling and its creator's
current membership. Demoting somebody narrows every token they ever minted, with
nothing to hunt down — which is what makes minting one a developer-level action
rather than an admin one.

## Consequences

- Password verification costs about 100 ms and 64 MiB. That is the point, and it
  is why the login endpoint sits behind a per-IP limiter as well as a per-account
  backoff.
- Every deployment must set `Auth:SigningKey` from a secret store. The
  application refuses to start without one rather than generating one, because a
  generated key silently invalidates every session on restart and differs
  between instances.
- Rate limiting is in-process, so the limit is per instance. Acceptable for
  protecting a host, unacceptable for anything billed; it needs a shared store
  before the second instance exists.
- OAuth is wired but unproven end to end. Account linking is tested directly; no
  test completes a real authorisation code exchange, because that needs live
  credentials at both providers.

## Alternatives considered

**Auth0 or Clerk.** Rejected for now: core, long-lived, cheap to own. Revisit at
SSO.

**PBKDF2, ASP.NET Core Identity's default.** Still defensible and not what the
engineering standards ask for. Argon2id is the current recommendation and
Konscious is a thin managed implementation of it; the alternative was writing a
KDF ourselves, which the standards say never to do.

**Opaque access tokens with a server-side session store.** Would remove the
fifteen-minute window entirely, at the cost of a store lookup on every request
and a shared store before the second instance. Revisit if the window proves too
long.

**Encoding roles in the row-level security policies.** Rejected: the permission
model would then live in two places that can disagree, and the copy in SQL would
be the one nobody remembers to update. RLS answers "whose rows"; the
authorisation layer answers "may they do this".
