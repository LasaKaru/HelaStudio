-- Reverses RowLevelSecurity.up.sql.
--
-- Policies are dropped with the tables' RLS still enabled for a moment, which
-- would deny everything; disabling RLS first keeps the intermediate state
-- readable if a rollback is interrupted.

ALTER TABLE audit_events    DISABLE ROW LEVEL SECURITY;
ALTER TABLE assets          DISABLE ROW LEVEL SECURITY;
ALTER TABLE config_versions DISABLE ROW LEVEL SECURITY;
ALTER TABLE apps            DISABLE ROW LEVEL SECURITY;
ALTER TABLE workspaces      DISABLE ROW LEVEL SECURITY;
ALTER TABLE org_members     DISABLE ROW LEVEL SECURITY;
ALTER TABLE orgs            DISABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS audit_events_tenant    ON audit_events;
DROP POLICY IF EXISTS assets_tenant          ON assets;
DROP POLICY IF EXISTS config_versions_tenant ON config_versions;
DROP POLICY IF EXISTS apps_tenant            ON apps;
DROP POLICY IF EXISTS workspaces_tenant      ON workspaces;
DROP POLICY IF EXISTS org_members_delete     ON org_members;
DROP POLICY IF EXISTS org_members_update     ON org_members;
DROP POLICY IF EXISTS org_members_insert     ON org_members;
DROP POLICY IF EXISTS org_members_read       ON org_members;
DROP POLICY IF EXISTS orgs_delete            ON orgs;
DROP POLICY IF EXISTS orgs_update            ON orgs;
DROP POLICY IF EXISTS orgs_insert            ON orgs;
DROP POLICY IF EXISTS orgs_read              ON orgs;

REVOKE ALL ON users           FROM shellwright_app;
REVOKE ALL ON audit_events    FROM shellwright_app;
REVOKE ALL ON config_versions FROM shellwright_app;
REVOKE ALL ON assets          FROM shellwright_app;
REVOKE ALL ON apps            FROM shellwright_app;
REVOKE ALL ON workspaces      FROM shellwright_app;
REVOKE ALL ON org_members     FROM shellwright_app;
REVOKE ALL ON orgs            FROM shellwright_app;

DROP FUNCTION IF EXISTS app_org_is_unclaimed(uuid);
DROP FUNCTION IF EXISTS app_member_app_ids();
DROP FUNCTION IF EXISTS app_member_workspace_ids();
DROP FUNCTION IF EXISTS app_member_org_ids();
DROP FUNCTION IF EXISTS app_current_user_id();
