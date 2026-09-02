namespace Shellwright.Api.Domain;

/// <summary>
/// The remembered outcome of a request that carried an <c>Idempotency-Key</c>.
/// </summary>
/// <remarks>
/// ⚠️ The stored request fingerprint is what makes this safe. Replaying a key
/// with a *different* body is not a retry, it is a bug or an attack, and
/// returning the first response would silently discard the second request. Such
/// a replay is refused rather than served.
/// </remarks>
public sealed class IdempotencyRecord
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Whose request it was. Keys are scoped per caller, never global.</summary>
    public Guid UserId { get; set; }

    /// <summary>The key the client sent.</summary>
    public required string Key { get; set; }

    /// <summary>Method and path, so the same key on a different endpoint is a different record.</summary>
    public required string Endpoint { get; set; }

    /// <summary>SHA-256 of the request body, hex.</summary>
    public required string RequestHash { get; set; }

    /// <summary>The status the first attempt returned.</summary>
    public int StatusCode { get; set; }

    /// <summary>The body the first attempt returned, replayed verbatim.</summary>
    public required string ResponseBody { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the record stops being honoured.
    /// </summary>
    /// <remarks>
    /// Twenty-four hours. Long enough to cover any retry a client or a proxy
    /// will make, short enough that the table does not become a permanent
    /// second copy of every response the API has ever sent.
    /// </remarks>
    public DateTimeOffset ExpiresAt { get; set; }
}
