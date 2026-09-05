-- The one identity-free read in the API.
--
-- ⚠️ A signed download link is followed by a browser, a `curl` in somebody's
-- CI, or an emulator — none of which carries a bearer token. So the endpoint
-- that serves it is anonymous, which means no identity is stamped on the
-- connection, which means every row-level security policy correctly hides
-- everything from it.
--
-- The wrong fixes, and why:
--
--   * Give the API a second role that bypasses policies. That role would be
--     available to every other handler in the process, and the next person to
--     need "just one query" would use it.
--   * Give the API the shellwright_runner credentials. Same problem, and it
--     widens a role that was deliberately scoped for a different service.
--
-- What is here instead is one function that answers exactly one question, takes
-- both identifiers so it cannot be used to enumerate, returns three columns and
-- no more, and is granted to the application role alone. It is reviewable
-- because it is small and named; a bypassing connection is not reviewable at
-- all.
--
-- It returns nothing for a build with no artifact, so the endpoint's "not
-- found" is the same answer for "no such build" and "nothing to download" —
-- an unauthenticated endpoint that distinguishes them is an oracle for which
-- build ids exist.
CREATE FUNCTION app_artifact_for_download(app uuid, build uuid)
    RETURNS TABLE (artifact_reference text, app_name text, platform integer)
    LANGUAGE sql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, public
    AS $$
        SELECT b.artifact_reference, a.name, b.platform
        FROM builds b
        JOIN apps a ON a.id = b.app_id
        WHERE b.id = build
          AND b.app_id = app
          AND b.artifact_reference IS NOT NULL
    $$;

REVOKE EXECUTE ON FUNCTION app_artifact_for_download(uuid, uuid) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION app_artifact_for_download(uuid, uuid) TO shellwright_app;
