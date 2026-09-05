using System.ComponentModel.DataAnnotations;

namespace Shellwright.Api.Auth;

/// <summary>Authentication settings.</summary>
public sealed class AuthOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Auth";

    /// <summary>
    /// Signing key for access tokens, base64.
    /// </summary>
    /// <remarks>
    /// ⚠️ Must be at least 32 bytes and must come from a secret store, never
    /// from <c>appsettings.json</c>. <c>AuthOptionsValidator</c> refuses to
    /// start without it rather than generating one, because a generated key
    /// would silently invalidate every session on each restart and would differ
    /// between instances.
    /// </remarks>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Token issuer, echoed in the <c>iss</c> claim.</summary>
    [Required]
    public string Issuer { get; set; } = "https://api.shellwright.dev";

    /// <summary>Intended audience, echoed in the <c>aud</c> claim.</summary>
    [Required]
    public string Audience { get; set; } = "shellwright";

    /// <summary>How long an access token is good for.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a refresh family lives before a fresh sign-in is required.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long an emailed verification or reset link is good for.</summary>
    public TimeSpan EmailTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Consecutive failures before an account starts backing off.</summary>
    public int LockoutThreshold { get; set; } = 5;

    /// <summary>Base of the exponential backoff applied past the threshold.</summary>
    public TimeSpan LockoutBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling on the backoff, so an account is never locked out permanently.</summary>
    public TimeSpan LockoutMaxDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Where the studio lives, for post-OAuth redirects.</summary>
    [Required]
    public string StudioOrigin { get; set; } = "http://localhost:5173";

    /// <summary>Configured OAuth providers, keyed by provider name.</summary>
    public Dictionary<string, OAuthProviderOptions> Providers { get; } = [];
}

/// <summary>Credentials and endpoints for one OAuth provider.</summary>
public sealed class OAuthProviderOptions
{
    /// <summary>Client identifier issued by the provider.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret issued by the provider.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Whether the provider is wired up. False when credentials are absent.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
