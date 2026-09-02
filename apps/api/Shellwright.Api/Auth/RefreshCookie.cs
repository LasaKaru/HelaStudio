using Microsoft.Extensions.Options;

namespace Shellwright.Api.Auth;

/// <summary>Reads and writes the cookie that carries the refresh token.</summary>
/// <remarks>
/// <para>
/// ⚠️ The refresh token is in a cookie and the access token is not, and that
/// asymmetry is the whole design. Script running in the studio's origin can
/// read anything the studio can read, so a refresh token in local storage is a
/// refresh token an XSS bug hands over — and a refresh token is a session that
/// renews itself for thirty days. <c>HttpOnly</c> puts it somewhere script
/// cannot reach; the fifteen-minute access token is what script holds instead,
/// and it expires on its own.
/// </para>
/// <para>
/// <c>SameSite=Lax</c> rather than <c>Strict</c>: the OAuth callback is a
/// cross-site top-level navigation back into this API, and Strict would drop
/// the cookie on exactly that request. Lax still refuses to send it on
/// cross-site subrequests, which is where CSRF lives.
/// </para>
/// <para>
/// The path confines it to the endpoints that consume it, so it is not attached
/// to every config read for the rest of the session.
/// </para>
/// </remarks>
/// <param name="options">Authentication settings.</param>
public sealed class RefreshCookie(IOptions<AuthOptions> options)
{
    /// <summary>Cookie name.</summary>
    public const string Name = "sw_refresh";

    /// <summary>Path the cookie is scoped to.</summary>
    public const string Path = "/v1/auth";

    private readonly AuthOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Writes the cookie.</summary>
    /// <param name="response">The response to write to.</param>
    /// <param name="secret">The refresh secret.</param>
    /// <param name="expiresAt">When the token expires.</param>
    public void Write(HttpResponse response, string secret, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Append(Name, secret, new CookieOptions
        {
            HttpOnly = true,

            // Always, including in development. Browsers treat http://localhost
            // as a secure context, so this costs nothing locally and removes
            // the chance of shipping a configuration switch set the wrong way.
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = Path,
            Expires = expiresAt,
            IsEssential = true,
        });
    }

    /// <summary>Removes the cookie.</summary>
    /// <param name="response">The response to write to.</param>
    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = Path,
        });
    }

    /// <summary>Reads the cookie.</summary>
    /// <param name="request">The request to read from.</param>
    /// <returns>The secret, or null when the cookie is absent.</returns>
    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(Name, out var value) ? value : null;
    }

    /// <summary>The configured studio origin, used for post-OAuth redirects.</summary>
    public string StudioOrigin => settings.StudioOrigin;
}
