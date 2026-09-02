-- Reverses Authentication.up.sql.

ALTER TABLE security_events DISABLE ROW LEVEL SECURITY;
ALTER TABLE api_tokens      DISABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS security_events_append ON security_events;
DROP POLICY IF EXISTS api_tokens_tenant      ON api_tokens;

DROP FUNCTION IF EXISTS app_resolve_api_token(text);

REVOKE ALL ON oauth_identities FROM shellwright_app;
REVOKE ALL ON user_tokens      FROM shellwright_app;
REVOKE ALL ON refresh_tokens   FROM shellwright_app;
REVOKE ALL ON api_tokens       FROM shellwright_app;
REVOKE ALL ON security_events  FROM shellwright_app;
