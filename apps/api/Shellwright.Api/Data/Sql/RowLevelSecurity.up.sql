-- Tenant isolation, enforced by the database.
--
-- The guarantee this file buys: a missing `WHERE org_id = ...` in application
-- code is a bug that returns too few rows, never too many. Application-level
-- scoping is still written everywhere it belongs — this is the floor beneath
-- it, not a replacement for it.
--
-- Three things have to hold for any of it to work, and all three are asserted
-- by RowLevelSecurityTests against a live database:
--
--   1. The API connects as shellwright_app.
--   2. shellwright_app owns none of these tables. A table's owner is exempt
--      from its own policies unless FORCE ROW LEVEL SECURITY is set.
--   3. shellwright_app does not hold BYPASSRLS.
--
-- Miss any one of them and every policy below becomes decoration, with no
-- outward sign that anything is wrong.

--------------------------------------------------------------------------------
-- Identity
--------------------------------------------------------------------------------

-- The current principal, as stamped onto the connection by
-- TenantConnectionInterceptor.
--
-- `current_setting(..., true)` returns NULL rather than raising when the
-- setting was never applied, and NULLIF turns the empty string into NULL as
-- well. Both cases mean "nobody", and every policy below compares against this
-- value, so nobody sees nothing: a comparison against NULL is never true.
CREATE FUNCTION app_current_user_id() RETURNS uuid
    LANGUAGE sql
    STABLE
    PARALLEL SAFE
    SET search_path = pg_catalog, public
    AS $$ SELECT NULLIF(current_setting('app.user_id', true), '')::uuid $$;

--------------------------------------------------------------------------------
-- Membership lookups
--------------------------------------------------------------------------------

-- ⚠️ SECURITY DEFINER, and not as an optimisation.
--
-- org_members carries a policy that is itself expressed in terms of
-- membership. A policy on a table that queries the same table recurses without
-- these functions: Postgres applies the policy to the reference inside the
-- policy, forever. Running as the owner steps outside RLS exactly once, at a
-- point where the query is fixed and cannot be influenced by the caller.
--
-- The functions take no arguments and filter on app_current_user_id(), so a
-- caller cannot ask them about somebody else.
CREATE FUNCTION app_member_org_ids() RETURNS SETOF uuid
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$
        SELECT m.org_id
        FROM org_members m
        JOIN orgs o ON o.id = m.org_id
        WHERE m.user_id = app_current_user_id()
          AND o.deleted_at IS NULL
    $$;

CREATE FUNCTION app_member_workspace_ids() RETURNS SETOF uuid
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$ SELECT w.id FROM workspaces w WHERE w.org_id IN (SELECT app_member_org_ids()) $$;

CREATE FUNCTION app_member_app_ids() RETURNS SETOF uuid
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$ SELECT a.id FROM apps a WHERE a.workspace_id IN (SELECT app_member_workspace_ids()) $$;

-- Bootstrapping: an organisation nobody belongs to yet may be claimed by its
-- creator. Without this, creating an organisation is impossible — the row is
-- invisible to you until you are a member, and you cannot become a member of
-- a row you cannot see. The window is exactly one member wide and closes the
-- moment it is used.
CREATE FUNCTION app_org_is_unclaimed(org uuid) RETURNS boolean
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$ SELECT NOT EXISTS (SELECT 1 FROM org_members m WHERE m.org_id = org) $$;

-- SECURITY DEFINER functions are granted to PUBLIC by default. Revoke first,
-- then grant to the one role that needs them.
REVOKE EXECUTE ON FUNCTION app_current_user_id() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION app_member_org_ids() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION app_member_workspace_ids() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION app_member_app_ids() FROM PUBLIC;
REVOKE EXECUTE ON FUNCTION app_org_is_unclaimed(uuid) FROM PUBLIC;

GRANT EXECUTE ON FUNCTION app_current_user_id() TO shellwright_app;
GRANT EXECUTE ON FUNCTION app_member_org_ids() TO shellwright_app;
GRANT EXECUTE ON FUNCTION app_member_workspace_ids() TO shellwright_app;
GRANT EXECUTE ON FUNCTION app_member_app_ids() TO shellwright_app;
GRANT EXECUTE ON FUNCTION app_org_is_unclaimed(uuid) TO shellwright_app;

--------------------------------------------------------------------------------
-- Policies
--------------------------------------------------------------------------------
--
-- RLS answers one question: which tenant's rows is this connection allowed to
-- touch at all. It deliberately does not encode the role matrix — a viewer and
-- an owner see the same rows here, and the difference between them is enforced
-- by the authorisation layer. Encoding roles in policies too would mean the
-- permission model lived in two places that could disagree, and the one in SQL
-- would be the one nobody remembered to update.

ALTER TABLE orgs ENABLE ROW LEVEL SECURITY;

CREATE POLICY orgs_read ON orgs FOR SELECT
    USING (id IN (SELECT app_member_org_ids()));

-- Anyone signed in may create an organisation; they then have to claim it in
-- the same transaction to see it again.
CREATE POLICY orgs_insert ON orgs FOR INSERT
    WITH CHECK (app_current_user_id() IS NOT NULL);

CREATE POLICY orgs_update ON orgs FOR UPDATE
    USING (id IN (SELECT app_member_org_ids()))
    WITH CHECK (id IN (SELECT app_member_org_ids()));

CREATE POLICY orgs_delete ON orgs FOR DELETE
    USING (id IN (SELECT app_member_org_ids()));

ALTER TABLE org_members ENABLE ROW LEVEL SECURITY;

CREATE POLICY org_members_read ON org_members FOR SELECT
    USING (org_id IN (SELECT app_member_org_ids()));

CREATE POLICY org_members_insert ON org_members FOR INSERT
    WITH CHECK (
        org_id IN (SELECT app_member_org_ids())
        OR (user_id = app_current_user_id() AND app_org_is_unclaimed(org_id))
    );

CREATE POLICY org_members_update ON org_members FOR UPDATE
    USING (org_id IN (SELECT app_member_org_ids()))
    WITH CHECK (org_id IN (SELECT app_member_org_ids()));

CREATE POLICY org_members_delete ON org_members FOR DELETE
    USING (org_id IN (SELECT app_member_org_ids()));

ALTER TABLE workspaces ENABLE ROW LEVEL SECURITY;

CREATE POLICY workspaces_tenant ON workspaces
    USING (org_id IN (SELECT app_member_org_ids()))
    WITH CHECK (org_id IN (SELECT app_member_org_ids()));

ALTER TABLE apps ENABLE ROW LEVEL SECURITY;

CREATE POLICY apps_tenant ON apps
    USING (workspace_id IN (SELECT app_member_workspace_ids()))
    WITH CHECK (workspace_id IN (SELECT app_member_workspace_ids()));

ALTER TABLE config_versions ENABLE ROW LEVEL SECURITY;

CREATE POLICY config_versions_tenant ON config_versions
    USING (app_id IN (SELECT app_member_app_ids()))
    WITH CHECK (app_id IN (SELECT app_member_app_ids()));

ALTER TABLE assets ENABLE ROW LEVEL SECURITY;

CREATE POLICY assets_tenant ON assets
    USING (org_id IN (SELECT app_member_org_ids()))
    WITH CHECK (org_id IN (SELECT app_member_org_ids()));

ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;

CREATE POLICY audit_events_tenant ON audit_events
    USING (org_id IN (SELECT app_member_org_ids()))
    WITH CHECK (org_id IN (SELECT app_member_org_ids()));

--------------------------------------------------------------------------------
-- Privileges
--------------------------------------------------------------------------------
--
-- The application role gets the narrowest set of verbs each table actually
-- needs. Two of them are narrower than the code requires today, on purpose:
-- config_versions and audit_events are append-only, and withholding UPDATE and
-- DELETE turns that from a convention into something the database refuses.
-- A future handler that tries to "just fix" a stored config fails immediately
-- and visibly, rather than silently invalidating every build cached against it.

GRANT SELECT, INSERT, UPDATE, DELETE ON orgs         TO shellwright_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON org_members  TO shellwright_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON workspaces   TO shellwright_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON apps         TO shellwright_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON assets       TO shellwright_app;

GRANT SELECT, INSERT ON config_versions TO shellwright_app;
GRANT SELECT, INSERT ON audit_events    TO shellwright_app;

-- users has no policy because it is not tenant-scoped: one account belongs to
-- many organisations, so there is no tenant to isolate it to. Access is
-- governed by the API, which never returns a row for anyone but the caller.
-- No DELETE: erasing an account is a deliberate, audited operation with its own
-- tooling, not something a request handler should be able to reach.
GRANT SELECT, INSERT, UPDATE ON users TO shellwright_app;
