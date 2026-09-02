REVOKE ALL ON apps              FROM shellwright_runner;
REVOKE ALL ON config_versions   FROM shellwright_runner;
REVOKE ALL ON usage_records     FROM shellwright_runner;
REVOKE ALL ON artifact_cache    FROM shellwright_runner;
REVOKE ALL ON build_transitions FROM shellwright_runner;
REVOKE ALL ON builds            FROM shellwright_runner;

DROP POLICY IF EXISTS apps_runner              ON apps;
DROP POLICY IF EXISTS config_versions_runner   ON config_versions;
DROP POLICY IF EXISTS usage_records_runner     ON usage_records;
DROP POLICY IF EXISTS artifact_cache_runner    ON artifact_cache;
DROP POLICY IF EXISTS build_transitions_runner ON build_transitions;
DROP POLICY IF EXISTS builds_runner            ON builds;

REVOKE USAGE ON SCHEMA public FROM shellwright_runner;
