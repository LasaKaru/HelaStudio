REVOKE ALL ON usage_records     FROM shellwright_app;
REVOKE ALL ON artifact_cache    FROM shellwright_app;
REVOKE ALL ON build_transitions FROM shellwright_app;
REVOKE ALL ON builds            FROM shellwright_app;

DROP POLICY IF EXISTS usage_records_tenant     ON usage_records;
DROP POLICY IF EXISTS artifact_cache_tenant    ON artifact_cache;
DROP POLICY IF EXISTS build_transitions_tenant ON build_transitions;
DROP POLICY IF EXISTS builds_tenant            ON builds;

ALTER TABLE usage_records     DISABLE ROW LEVEL SECURITY;
ALTER TABLE artifact_cache    DISABLE ROW LEVEL SECURITY;
ALTER TABLE build_transitions DISABLE ROW LEVEL SECURITY;
ALTER TABLE builds            DISABLE ROW LEVEL SECURITY;
