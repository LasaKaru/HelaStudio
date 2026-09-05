-- What the build orchestrator is allowed to touch.
--
-- ⚠️ The orchestrator acts for no particular user. It runs builds for
-- everybody, so it cannot be scoped by membership the way the API is — there is
-- no "current user" to compare a row against.
--
-- The tempting answer is BYPASSRLS, and it is the wrong one. A role that
-- bypasses row-level security is unbounded by construction: it can read every
-- user, every organisation, every API token hash and every refresh token, and
-- nothing in the schema records that this was ever considered. What is written
-- here instead is a role whose reach over *tenants* is total and whose reach
-- over *tables* is a short list, so the blast radius of a compromised
-- orchestrator is legible rather than "everything".
--
-- Notably absent, and each one deliberately: users, orgs, org_members,
-- workspaces, assets, api_tokens, refresh_tokens, user_tokens, oauth_identities,
-- audit_events, security_events, idempotency_records.

-- The role is created by deployment (scripts/dev-postgres.sh locally), because
-- a migration cannot know its password. This is the guard for a database where
-- it was not.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'shellwright_runner') THEN
        RAISE EXCEPTION
            'The shellwright_runner role does not exist. Create it before migrating: '
            'CREATE ROLE shellwright_runner LOGIN PASSWORD ''...'' NOBYPASSRLS NOINHERIT;';
    END IF;
END
$$;

GRANT USAGE ON SCHEMA public TO shellwright_runner;

--------------------------------------------------------------------------------
-- Policies
--------------------------------------------------------------------------------
--
-- ⚠️ Scoped TO shellwright_runner. Multiple permissive policies on a table are
-- OR'd together, so an unscoped `USING (true)` here would hand every tenant's
-- rows to the API role as well and silently undo the entire isolation model.
-- The `TO` clause is the only thing standing between these four statements and
-- that outcome.

CREATE POLICY builds_runner ON builds
    TO shellwright_runner
    USING (true)
    WITH CHECK (true);

CREATE POLICY build_transitions_runner ON build_transitions
    TO shellwright_runner
    USING (true)
    WITH CHECK (true);

CREATE POLICY artifact_cache_runner ON artifact_cache
    TO shellwright_runner
    USING (true)
    WITH CHECK (true);

CREATE POLICY usage_records_runner ON usage_records
    TO shellwright_runner
    USING (true)
    WITH CHECK (true);

-- The orchestrator compiles configurations, so it must read them. It must never
-- write one: a configuration is the customer's, and a build that could edit
-- what it was asked to build could produce an artifact nobody requested.
CREATE POLICY config_versions_runner ON config_versions
    FOR SELECT
    TO shellwright_runner
    USING (true);

-- Read-only on apps, to resolve which organisation a build is charged to and
-- to reject a build against an archived app.
CREATE POLICY apps_runner ON apps
    FOR SELECT
    TO shellwright_runner
    USING (true);

--------------------------------------------------------------------------------
-- Privileges
--------------------------------------------------------------------------------
--
-- The same verbs the API has on these tables, and no more. In particular
-- usage_records and build_transitions stay append-only: the orchestrator is the
-- thing that *writes* the meter, which is exactly why it must not be able to
-- rewrite it.

GRANT SELECT, INSERT, UPDATE ON builds          TO shellwright_runner;
GRANT SELECT, INSERT         ON build_transitions TO shellwright_runner;
GRANT SELECT, INSERT, UPDATE ON artifact_cache  TO shellwright_runner;
GRANT SELECT, INSERT         ON usage_records   TO shellwright_runner;

GRANT SELECT ON config_versions TO shellwright_runner;
GRANT SELECT ON apps            TO shellwright_runner;
