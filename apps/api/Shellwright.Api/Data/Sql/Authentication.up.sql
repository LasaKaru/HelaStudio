-- Policies and grants for the credential tables.
--
-- None of these are tenant-scoped in the way orgs and apps are: a refresh token
-- belongs to a person, not to an organisation, and the person may belong to
-- several. So the shape here is different — the API is trusted to scope its own
-- queries, and the database's job is to make the *destructive* operations
-- impossible rather than to filter rows.

-- api_tokens is the exception: it does belong to an organisation, and it is the
-- one credential another member of that organisation is entitled to see listed
-- and to revoke.
ALTER TABLE api_tokens ENABLE ROW LEVEL SECURITY;

CREATE POLICY api_tokens_tenant ON api_tokens
    USING (org_id IN (SELECT app_member_org_ids()))
    WITH CHECK (org_id IN (SELECT app_member_org_ids()));

-- ⚠️ security_events is write-only for the application role.
--
-- An INSERT policy with no SELECT policy means the API can append to its own
-- security log and cannot read it back, alter it, or erase it. That is the
-- property worth having: the component most likely to be compromised is the one
-- that cannot tamper with the record of the compromise. Reading it is an
-- operations task, done as the schema owner.
ALTER TABLE security_events ENABLE ROW LEVEL SECURITY;

CREATE POLICY security_events_append ON security_events FOR INSERT
    WITH CHECK (true);

GRANT INSERT ON security_events TO shellwright_app;

GRANT SELECT, INSERT, UPDATE, DELETE ON api_tokens TO shellwright_app;

-- ⚠️ Resolving a credential is the one lookup that cannot be scoped by the
-- identity it is trying to establish.
--
-- api_tokens carries a membership policy, and at the moment a request arrives
-- there is no member yet — the connection has no identity, so the policy
-- correctly returns nothing and the token can never be recognised. Rather than
-- weakening the policy for every query, exactly one query steps outside it.
--
-- The function takes a hash and returns a single row. It cannot be used to
-- enumerate: an argument that does not match a stored fingerprint returns
-- nothing, and the caller has to know a 256-bit secret to produce one that
-- does. The secret itself never reaches the database.
CREATE FUNCTION app_resolve_api_token(hash text)
    RETURNS TABLE (
        id uuid,
        org_id uuid,
        workspace_id uuid,
        role text,
        created_by uuid,
        revoked_at timestamptz,
        last_used_at timestamptz)
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$
        SELECT t.id, t.org_id, t.workspace_id, t.role, t.created_by, t.revoked_at, t.last_used_at
        FROM api_tokens t
        WHERE t.token_hash = hash
    $$;

REVOKE EXECUTE ON FUNCTION app_resolve_api_token(text) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app_resolve_api_token(text) TO shellwright_app;

-- refresh_tokens: UPDATE is needed for rotation and revocation, DELETE for the
-- expiry sweep. Both are legitimate here — unlike config_versions, this table
-- is state, not history. The history of what happened to it lives in
-- security_events, which cannot be edited.
GRANT SELECT, INSERT, UPDATE, DELETE ON refresh_tokens TO shellwright_app;

-- user_tokens: no DELETE. A redeemed token is marked consumed and kept until it
-- expires, so that presenting it a second time is distinguishable from
-- presenting one that never existed. Deleting on redemption would collapse
-- those two cases into the same silent failure.
GRANT SELECT, INSERT, UPDATE ON user_tokens TO shellwright_app;

GRANT SELECT, INSERT, DELETE ON oauth_identities TO shellwright_app;
