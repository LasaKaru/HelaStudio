namespace Shellwright.Api.Auth;

/// <summary>Claim names this API issues beyond the registered set.</summary>
/// <remarks>
/// Prefixed rather than bare, so that a claim minted here is never confused
/// with one an identity provider happened to use the same word for.
/// </remarks>
public static class ShellwrightClaims
{
    /// <summary>Identifier of the API token a request was authenticated with.</summary>
    public const string TokenId = "sw_tid";

    /// <summary>Organisation an API token is confined to.</summary>
    public const string TokenOrg = "sw_org";

    /// <summary>Workspace an API token is confined to, when it is.</summary>
    public const string TokenWorkspace = "sw_ws";

    /// <summary>Ceiling on what an API token may do.</summary>
    public const string TokenRole = "sw_role";
}

/// <summary>Authentication scheme names.</summary>
public static class AuthSchemes
{
    /// <summary>Chooses between the two real schemes by looking at the token.</summary>
    public const string Any = "shellwright";

    /// <summary>Short-lived access tokens issued to the studio.</summary>
    public const string AccessToken = "access-token";

    /// <summary>Long-lived <c>sw_live_…</c> credentials used by CI and the command line.</summary>
    public const string ApiToken = "api-token";

    /// <summary>
    /// Holds the provider's ticket for the few milliseconds between the OAuth
    /// callback and our own tokens being issued.
    /// </summary>
    /// <remarks>
    /// A cookie scheme is required because the OAuth handler signs in to one.
    /// It is never used to authorise anything: the callback handler reads the
    /// ticket, exchanges it for a Shellwright session, and signs straight back
    /// out.
    /// </remarks>
    public const string OAuthHandoff = "oauth-handoff";
}
