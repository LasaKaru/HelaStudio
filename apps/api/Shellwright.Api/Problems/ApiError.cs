namespace Shellwright.Api.Problems;

/// <summary>
/// Every error this API can return, with the status and wording that go with it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ One catalogue, because the <c>type</c> URI in an RFC 9457 response is a
/// promise. A client is entitled to branch on it, and a documentation page is
/// expected to exist at the other end of it. Generating those URIs at each call
/// site would produce a set nobody can enumerate, which is the same as having
/// none: the field would carry a string that changes whenever somebody
/// rewords a message.
/// </para>
/// <para>
/// The naming deliberately matches the configuration diagnostics from Sprint 01
/// — <c>CFG_*</c> for a document, <c>API_*</c> for a request — so that the same
/// code means the same thing in the studio, in the API, and in the runner.
/// </para>
/// </remarks>
public sealed record ApiError(string Code, int Status, string Title)
{
    /// <summary>Where the documentation for these codes lives.</summary>
    public const string DocsBase = "https://docs.shellwright.dev/reference/api-errors";

    /// <summary>The stable, dereferenceable identifier for this error.</summary>
    /// <remarks>
    /// Derived rather than stored, so a code and its URI cannot drift apart.
    /// </remarks>
#pragma warning disable CA1308 // A documentation anchor built from an ASCII constant, not a security comparison.
    public string Type => $"{DocsBase}#{Code.ToLowerInvariant()}";
#pragma warning restore CA1308
}

/// <summary>The catalogue.</summary>
public static class ApiErrors
{
    // ── Authentication ──────────────────────────────────────────────────────

    /// <summary>
    /// The credentials did not match.
    /// </summary>
    /// <remarks>
    /// ⚠️ One code for "no such account" and for "wrong password", deliberately.
    /// Two codes would be an account-existence oracle with a machine-readable
    /// interface.
    /// </remarks>
    public static ApiError InvalidCredentials { get; } =
        new("API_INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized, "Invalid credentials");

    /// <summary>Too many recent failures; the account is backing off.</summary>
    public static ApiError TooManyAttempts { get; } =
        new("API_TOO_MANY_ATTEMPTS", StatusCodes.Status429TooManyRequests, "Too many attempts");

    /// <summary>
    /// The session is over.
    /// </summary>
    /// <remarks>
    /// ⚠️ Covers expiry, revocation, and detected token reuse alike. Separating
    /// them would tell an attacker holding a replayed token that the replay is
    /// what gave them away.
    /// </remarks>
    public static ApiError SessionExpired { get; } =
        new("API_SESSION_EXPIRED", StatusCodes.Status401Unauthorized, "Session expired");

    /// <summary>An emailed verification or reset link is no longer valid.</summary>
    public static ApiError LinkExpired { get; } =
        new("API_LINK_EXPIRED", StatusCodes.Status400BadRequest, "Link expired");

    /// <summary>The identity provider did not finish, or returned nothing usable.</summary>
    public static ApiError SignInIncomplete { get; } =
        new("API_SIGN_IN_INCOMPLETE", StatusCodes.Status401Unauthorized, "Sign-in did not complete");

    /// <summary>The named identity provider is not configured on this deployment.</summary>
    public static ApiError UnknownProvider { get; } =
        new("API_UNKNOWN_PROVIDER", StatusCodes.Status404NotFound, "Unknown provider");

    // ── Authorisation ───────────────────────────────────────────────────────

    /// <summary>Authentication is required and was not supplied.</summary>
    public static ApiError Unauthenticated { get; } =
        new("API_UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Not signed in");

    /// <summary>The caller can see the resource but may not do this to it.</summary>
    public static ApiError Forbidden { get; } =
        new("API_FORBIDDEN", StatusCodes.Status403Forbidden, "Not allowed");

    /// <summary>
    /// The resource does not exist, or exists somewhere the caller has no part in.
    /// </summary>
    /// <remarks>
    /// ⚠️ Those two cases share a code for the same reason they share a status:
    /// distinguishing them confirms that an identifier is real.
    /// </remarks>
    public static ApiError NotFound { get; } =
        new("API_NOT_FOUND", StatusCodes.Status404NotFound, "Not found");

    // ── Requests ────────────────────────────────────────────────────────────

    /// <summary>The body was not parseable JSON.</summary>
    public static ApiError MalformedJson { get; } =
        new("API_MALFORMED_JSON", StatusCodes.Status400BadRequest, "Malformed JSON");

    /// <summary>The request was well formed and its contents were not acceptable.</summary>
    public static ApiError ValidationFailed { get; } =
        new("API_VALIDATION_FAILED", StatusCodes.Status422UnprocessableEntity, "Request is not valid");

    /// <summary>An <c>Idempotency-Key</c> was reused for a different body.</summary>
    public static ApiError IdempotencyKeyReused { get; } =
        new("API_IDEMPOTENCY_KEY_REUSED", StatusCodes.Status409Conflict, "Idempotency key reused");

    /// <summary>The upload exceeded the size limit.</summary>
    public static ApiError PayloadTooLarge { get; } =
        new("API_PAYLOAD_TOO_LARGE", StatusCodes.Status413PayloadTooLarge, "Too large");

    /// <summary>The caller is making requests faster than the limit allows.</summary>
    public static ApiError RateLimited { get; } =
        new("API_RATE_LIMITED", StatusCodes.Status429TooManyRequests, "Too many requests");

    // ── Resources ───────────────────────────────────────────────────────────

    /// <summary>A slug or bundle id is already in use.</summary>
    public static ApiError NameTaken { get; } =
        new("API_NAME_TAKEN", StatusCodes.Status409Conflict, "Name taken");

    /// <summary>The configuration document did not validate.</summary>
    /// <remarks>
    /// The per-diagnostic <c>CFG_*</c> codes travel in the <c>errors</c> array;
    /// this is the envelope that says the request failed because of them.
    /// </remarks>
    public static ApiError ConfigInvalid { get; } =
        new("API_CONFIG_INVALID", StatusCodes.Status422UnprocessableEntity, "Configuration is not valid");

    /// <summary>The app has no configuration yet.</summary>
    public static ApiError NoConfiguration { get; } =
        new("API_NO_CONFIGURATION", StatusCodes.Status404NotFound, "No configuration");

    /// <summary>
    /// A creating request arrived without an <c>Idempotency-Key</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ Required rather than optional on builds, unlike everywhere else in
    /// this API. A retried save costs a duplicate row that the content address
    /// collapses anyway; a retried build costs runner minutes somebody is
    /// billed for. Making the client name the request is the only way the
    /// server can tell "start another build" from "I did not hear you".
    /// </remarks>
    public static ApiError IdempotencyKeyRequired { get; } =
        new("API_IDEMPOTENCY_KEY_REQUIRED", StatusCodes.Status400BadRequest, "Idempotency key required");

    /// <summary>The organisation already has as many builds running as it may.</summary>
    public static ApiError BuildConcurrencyExceeded { get; } =
        new("API_BUILD_CONCURRENCY_EXCEEDED", StatusCodes.Status429TooManyRequests, "Too many builds running");

    /// <summary>The build has already finished, so there is nothing to cancel.</summary>
    public static ApiError BuildNotCancellable { get; } =
        new("API_BUILD_NOT_CANCELLABLE", StatusCodes.Status409Conflict, "Build already finished");

    /// <summary>The build produced no artifact to download.</summary>
    public static ApiError NoArtifact { get; } =
        new("API_NO_ARTIFACT", StatusCodes.Status404NotFound, "No artifact");

    /// <summary>The download link is expired or was not issued by this server.</summary>
    public static ApiError InvalidDownloadLink { get; } =
        new("API_INVALID_DOWNLOAD_LINK", StatusCodes.Status403Forbidden, "Link is not valid");

    /// <summary>The app has been archived and cannot be built.</summary>
    public static ApiError AppArchived { get; } =
        new("API_APP_ARCHIVED", StatusCodes.Status409Conflict, "App is archived");

    /// <summary>Demoting this member would leave the organisation with no owner.</summary>
    public static ApiError LastOwner { get; } =
        new("API_LAST_OWNER", StatusCodes.Status409Conflict, "Last owner");

    /// <summary>Something went wrong that the caller cannot act on.</summary>
    public static ApiError Internal { get; } =
        new("API_INTERNAL", StatusCodes.Status500InternalServerError, "Something went wrong");

    /// <summary>Every error in the catalogue, for the documentation and the coverage test.</summary>
    public static IReadOnlyList<ApiError> All { get; } =
    [
        InvalidCredentials,
        TooManyAttempts,
        SessionExpired,
        LinkExpired,
        SignInIncomplete,
        UnknownProvider,
        Unauthenticated,
        Forbidden,
        NotFound,
        MalformedJson,
        ValidationFailed,
        IdempotencyKeyReused,
        PayloadTooLarge,
        RateLimited,
        NameTaken,
        ConfigInvalid,
        NoConfiguration,
        IdempotencyKeyRequired,
        BuildConcurrencyExceeded,
        BuildNotCancellable,
        NoArtifact,
        InvalidDownloadLink,
        AppArchived,
        LastOwner,
        Internal,
    ];
}
