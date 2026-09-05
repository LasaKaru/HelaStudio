using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shellwright.Api.Email;

namespace Shellwright.Api.Auth;

/// <summary>Registers authentication.</summary>
public static class AuthServiceCollectionExtensions
{
    /// <summary>Adds the two token schemes, the OAuth providers, and the services behind them.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightAuth(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => TryValidateSigningKey(options.SigningKey),
                "Auth:SigningKey must decode to at least 32 bytes. Generate one with `openssl rand -base64 32`.")
            .ValidateOnStart();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTimeProvider();

        services.AddSingleton<PasswordHasher>();
        services.AddScoped<AccessTokenIssuer>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<ApiTokenService>();
        services.AddScoped<UserTokenService>();
        services.AddScoped<IdentityService>();
        services.AddScoped<RefreshCookie>();

        AddEmailSender(services, configuration);

        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services.AddAuthentication(AuthSchemes.Any)
            // ⚠️ One header, two credential formats. Rather than a second header
            // or a query parameter, the scheme is chosen by looking at the
            // token: anything starting sw_live_ is an API token and everything
            // else is a JWT. Callers do not have to know which they hold, and
            // adding a third format later is a change in one place.
            .AddPolicyScheme(AuthSchemes.Any, AuthSchemes.Any, policy =>
            {
                policy.ForwardDefaultSelector = context =>
                {
                    var header = context.Request.Headers.Authorization.ToString();

                    return header.StartsWith("Bearer " + ApiTokenService.LivePrefix, StringComparison.OrdinalIgnoreCase)
                        ? AuthSchemes.ApiToken
                        : AuthSchemes.AccessToken;
                };
            })
            .AddJwtBearer(AuthSchemes.AccessToken, jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = authOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = AccessTokenIssuer.CreateKey(authOptions.SigningKey),
                    ValidateLifetime = true,

                    // The default is five minutes, which quietly turns a
                    // fifteen-minute token into a twenty-minute one.
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    // Without this the identity reports itself as
                    // "AuthenticationTypes.Federation", which tells a caller
                    // nothing and makes the two schemes indistinguishable in
                    // logs and in /v1/auth/me.
                    AuthenticationType = AuthSchemes.AccessToken,
                    NameClaimType = "sub",
                    RoleClaimType = ClaimTypes.Role,
                };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                AuthSchemes.ApiToken,
                _ => { })
            .AddCookie(AuthSchemes.OAuthHandoff, cookie =>
            {
                cookie.Cookie.Name = "sw_oauth";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Lax;

                // Long enough to complete a redirect, short enough that an
                // abandoned attempt leaves nothing behind.
                cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                cookie.SlidingExpiration = false;
            })
            .AddConfiguredOAuthProviders(authOptions);

        // ⚠️ Validate lifetimes against the application's clock, not the
        // machine's.
        //
        // The library defaults to DateTime.UtcNow, which means the component
        // that decides whether a token has expired is the one place in the
        // system that ignores the injected TimeProvider. That is a testability
        // problem — expiry can only be exercised by sleeping — and a
        // correctness one: an issuer and a validator that disagree about the
        // time issue tokens that are already expired, or accept ones that are
        // not yet valid, and both failures look like a signing bug.
        services.AddOptions<JwtBearerOptions>(AuthSchemes.AccessToken)
            .Configure<TimeProvider>((jwt, clock) =>
                jwt.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, parameters) =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    var skew = parameters?.ClockSkew ?? TimeSpan.Zero;

                    return (notBefore is not { } start || start <= now + skew)
                        && (expires is not { } end || end + skew >= now);
                });

        return services;
    }

    /// <summary>Chooses a real email provider when one is configured, and says so when it is not.</summary>
    private static void AddEmailSender(IServiceCollection services, IConfiguration configuration)
    {
        var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();

        if (string.IsNullOrWhiteSpace(email.ApiKey))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            return;
        }

        services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", email.ApiKey);
            client.Timeout = TimeSpan.FromSeconds(10);
        });
    }

    private static bool TryValidateSigningKey(string signingKey)
    {
        try
        {
            AccessTokenIssuer.CreateKey(signingKey);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IServiceCollection TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(x => x.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}
