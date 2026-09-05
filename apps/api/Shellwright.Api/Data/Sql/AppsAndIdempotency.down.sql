-- Reverses AppsAndIdempotency.up.sql.

ALTER TABLE idempotency_keys DISABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS idempotency_keys_own ON idempotency_keys;
REVOKE ALL ON idempotency_keys FROM shellwright_app;
