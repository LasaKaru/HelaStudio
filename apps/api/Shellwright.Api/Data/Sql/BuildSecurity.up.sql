-- Tenant isolation for the build tables.
--
-- Same floor as RowLevelSecurity.up.sql and the same three preconditions: the
-- API connects as shellwright_app, shellwright_app owns none of these tables,
-- and shellwright_app does not hold BYPASSRLS. Miss any one and every policy
-- here is decoration, with no outward sign that anything is wrong.

ALTER TABLE builds ENABLE ROW LEVEL SECURITY;

CREATE POLICY builds_tenant ON builds
    USING (app_id IN (SELECT app_member_app_ids()))
    WITH CHECK (app_id IN (SELECT app_member_app_ids()));

ALTER TABLE build_transitions ENABLE ROW LEVEL SECURITY;

-- Scoped through the build rather than carrying its own app_id. A denormalised
-- column here would be a second place for the answer to live, and the two could
-- disagree — at which point the history says one tenant and the build says
-- another.
CREATE POLICY build_transitions_tenant ON build_transitions
    USING (build_id IN (SELECT b.id FROM builds b WHERE b.app_id IN (SELECT app_member_app_ids())))
    WITH CHECK (build_id IN (SELECT b.id FROM builds b WHERE b.app_id IN (SELECT app_member_app_ids())));

ALTER TABLE artifact_cache ENABLE ROW LEVEL SECURITY;

-- ⚠️ The most important policy in this file. A cache lookup answers "is there
-- an artifact I can hand back instead of building", and a leak here does not
-- leak a row — it hands one customer the compiled binary of another. The unique
-- index already scopes entries per app; this makes the scoping something the
-- database refuses to cross rather than something the query remembered.
CREATE POLICY artifact_cache_tenant ON artifact_cache
    USING (app_id IN (SELECT app_member_app_ids()))
    WITH CHECK (app_id IN (SELECT app_member_app_ids()));

ALTER TABLE usage_records ENABLE ROW LEVEL SECURITY;

CREATE POLICY usage_records_tenant ON usage_records
    USING (org_id IN (SELECT app_member_org_ids()))
    WITH CHECK (org_id IN (SELECT app_member_org_ids()));

--------------------------------------------------------------------------------
-- Privileges
--------------------------------------------------------------------------------

-- builds is mutable: a build moves through states and ends carrying an
-- artifact. No DELETE — a build row is the thing a usage row points at, and
-- removing one would leave a charge nobody can explain.
GRANT SELECT, INSERT, UPDATE ON builds TO shellwright_app;

-- ⚠️ Append-only by grant. build_transitions is the answer to "why did this
-- take eleven minutes", and a history that can be edited answers that question
-- with whatever somebody decided it should say.
GRANT SELECT, INSERT ON build_transitions TO shellwright_app;

-- UPDATE is granted for last_used_at alone, so eviction can be by real use
-- rather than by age. No DELETE: eviction is a deliberate, audited job with its
-- own credentials, not something a request handler can reach — an accidental
-- DELETE here silently turns every subsequent build into a full rebuild, and
-- the only symptom is the bill.
GRANT SELECT, INSERT, UPDATE ON artifact_cache TO shellwright_app;

-- ⚠️ INSERT and SELECT only. Nothing in the running system may un-bill a build
-- or quietly adjust what a customer owes; corrections are credits, written as
-- new rows, which is also how anybody auditing this later can see what happened.
GRANT SELECT, INSERT ON usage_records TO shellwright_app;
