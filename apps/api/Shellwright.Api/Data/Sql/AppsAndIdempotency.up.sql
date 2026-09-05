-- Idempotency records are scoped to one person, not to one tenant.
--
-- The predicate is a direct comparison rather than a membership lookup, so
-- there is no recursion to break and no helper function needed: a record
-- belongs to exactly the caller who created it, and nobody else — not even a
-- colleague in the same organisation — has any business reading it back.
ALTER TABLE idempotency_keys ENABLE ROW LEVEL SECURITY;

CREATE POLICY idempotency_keys_own ON idempotency_keys
    USING (user_id = app_current_user_id())
    WITH CHECK (user_id = app_current_user_id());

-- DELETE is granted for the expiry sweep. Unlike config_versions this table is
-- a cache, and a stale entry that cannot be removed is a slow leak of every
-- response body the API has ever sent.
GRANT SELECT, INSERT, DELETE ON idempotency_keys TO shellwright_app;
