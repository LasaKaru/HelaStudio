using System.Collections.Immutable;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Auth;

/// <summary>One external identity provider's endpoints and claim shape.</summary>
/// <param name="Scheme">Local scheme name, also the URL segment.</param>
/// <param name="AuthorizationEndpoint">Where the browser is sent.</param>
/// <param name="TokenEndpoint">Where the code is exchanged.</param>
/// <param name="UserInformationEndpoint">Where the profile is fetched.</param>
/// <param name="IdProperty">JSON property holding the provider's stable user id.</param>
/// <param name="EmailProperty">JSON property holding the address.</param>
/// <param name="Scopes">Scopes to request.</param>
public sealed record OAuthProviderDescriptor(
    string Scheme,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string UserInformationEndpoint,
    string IdProperty,
    string EmailProperty,
    ImmutableArray<string> Scopes);

/// <summary>
/// Sign-in through GitHub and Google.
/// </summary>
/// <remarks>
/// <para>
/// Built on the framework's generic <c>AddOAuth</c> handler rather than on a
/// per-provider package. Two providers do not justify two dependencies, and
/// writing the endpoints out means the exact URLs and scopes being requested
/// are visible in this file rather than inside somebody else's defaults.
/// </para>
/// <para>
/// ⚠️ Unverified end to end. Nothing in the test suite completes a real
/// authorisation code exchange, because doing so needs live credentials at both
/// providers. The account-linking half — matching on the provider's id,
/// creating or attaching a local account, issuing our own tokens — is covered
/// by tests that call <see cref="LinkAsync"/> directly. See
/// <c>ACTION_REQUIRED.md</c>.
/// </para>
/// </remarks>
public static class OAuthProviders
{
    /// <summary>GitHub. The audience for this product mostly already has an account.</summary>
    public static OAuthProviderDescriptor GitHub { get; } = new(
        "github",
        "https://github.com/login/oauth/authorize",
        "https://github.com/login/oauth/access_token",
        "https://api.github.com/user",
        "id",
        "email",
        ["read:user", "user:email"]);

    /// <summary>Google.</summary>
    public static OAuthProviderDescriptor Google { get; } = new(
        "google",
        "https://accounts.google.com/o/oauth2/v2/auth",
        "https://oauth2.googleapis.com/token",
        "https://www.googleapis.com/oauth2/v3/userinfo",
        "sub",
        "email",
        ["openid", "email", "profile"]);

    /// <summary>Every provider the API knows how to talk to.</summary>
    public static IReadOnlyList<OAuthProviderDescriptor> All { get; } = [GitHub, Google];

    /// <summary>Registers the configured providers.</summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="options">Authentication settings.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// A provider with no credentials is skipped rather than registered with
    /// empty ones. The alternative is an endpoint that exists, accepts a
    /// request, and fails at the provider with an error the user cannot act on.
    /// </remarks>
    public static AuthenticationBuilder AddConfiguredOAuthProviders(
        this AuthenticationBuilder builder,
        AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        foreach (var provider in All)
        {
            if (!options.Providers.TryGetValue(provider.Scheme, out var credentials) || !credentials.IsConfigured)
            {
                continue;
            }

            builder.AddOAuth(provider.Scheme, oauth =>
            {
                oauth.ClientId = credentials.ClientId;
                oauth.ClientSecret = credentials.ClientSecret;
                oauth.CallbackPath = $"/v1/auth/oauth/{provider.Scheme}/callback";
                oauth.AuthorizationEndpoint = provider.AuthorizationEndpoint;
                oauth.TokenEndpoint = provider.TokenEndpoint;
                oauth.UserInformationEndpoint = provider.UserInformationEndpoint;
                oauth.SignInScheme = AuthSchemes.OAuthHandoff;
                oauth.SaveTokens = false;
                oauth.UsePkce = true;

                foreach (var scope in provider.Scopes)
                {
                    oauth.Scope.Add(scope);
                }

                oauth.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, provider.IdProperty);
                oauth.ClaimActions.MapJsonKey(ClaimTypes.Email, provider.EmailProperty);

                oauth.Events.OnCreatingTicket = async context =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, oauth.UserInformationEndpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                    // GitHub rejects requests without one, and a missing header
                    // there produces a 403 that reads like a scope problem.
                    request.Headers.UserAgent.ParseAdd("Shellwright");

                    using var response = await context.Backchannel.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        context.HttpContext.RequestAborted);

                    response.EnsureSuccessStatusCode();

                    using var payload = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

                    context.RunClaimActions(payload.RootElement);
                };
            });
        }

        return builder;
    }

    /// <summary>
    /// Finds or creates the local account behind an external identity.
    /// </summary>
    /// <param name="database">The database context.</param>
    /// <param name="provider">Provider key.</param>
    /// <param name="providerUserId">The provider's stable identifier.</param>
    /// <param name="email">The address the provider reported, casefolded by the caller.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The local account.</returns>
    /// <remarks>
    /// <para>
    /// Matching is on <paramref name="providerUserId"/> first and on the address
    /// only when no link exists. That order matters: a provider account can
    /// change its address, and matching on address first would follow the
    /// address to whoever holds it now.
    /// </para>
    /// <para>
    /// ⚠️ Linking by address at all is a decision, not an oversight. It is what
    /// lets someone who signed up with a password later sign in with GitHub and
    /// land in the same account, rather than silently acquiring a second one.
    /// It is safe only because the providers we accept verify addresses; a
    /// provider that did not would let anyone claim any account by asserting
    /// its address, which is why the provider list is a fixed table in this
    /// file rather than configuration.
    /// </para>
    /// </remarks>
    public static async Task<User> LinkAsync(
        ShellwrightDbContext database,
        string provider,
        string providerUserId,
        string email,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);

        var normalised = IdentityService.NormaliseEmail(email);
        var now = clock.GetUtcNow();

        var linkedUserId = await database.OAuthIdentities
            .Where(x => x.Provider == provider && x.ProviderUserId == providerUserId)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (linkedUserId is { } existing)
        {
            return await database.Users.FirstAsync(x => x.Id == existing, cancellationToken);
        }

        var user = await database.Users.FirstOrDefaultAsync(x => x.Email == normalised, cancellationToken);

        if (user is null)
        {
            user = new User
            {
                Email = normalised,

                // No password. The account can gain one through the reset flow,
                // which proves control of the address the same way registration
                // does.
                PasswordHash = null,

                // The provider verified it; asking the user to verify it again
                // through us would be asking them to prove something we already
                // know.
                EmailVerifiedAt = now,
                CreatedAt = now,
            };

            database.Users.Add(user);
        }

        database.OAuthIdentities.Add(new OAuthIdentity
        {
            Provider = provider,
            ProviderUserId = providerUserId,
            UserId = user.Id,
            CreatedAt = now,
        });

        await database.SaveChangesAsync(cancellationToken);
        return user;
    }
}
