using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Shellwright.Api.Auth;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;
using Shellwright.Api.Email;

namespace Shellwright.Api.Endpoints;

/// <summary>Credentials in.</summary>
/// <param name="Email">Address.</param>
/// <param name="Password">Plaintext password.</param>
public sealed record CredentialsRequest(
    [param: Required, EmailAddress, StringLength(320)] string Email,
    [param: Required, StringLength(256, MinimumLength = 10)] string Password);

/// <summary>An address, for the forgotten-password flow.</summary>
/// <param name="Email">Address.</param>
public sealed record EmailRequest([param: Required, EmailAddress, StringLength(320)] string Email);

/// <summary>A token from an emailed link.</summary>
/// <param name="Token">The secret.</param>
public sealed record TokenRequest([param: Required, StringLength(200)] string Token);

/// <summary>A token and the password to set with it.</summary>
/// <param name="Token">The secret.</param>
/// <param name="Password">The new plaintext password.</param>
public sealed record ResetPasswordRequest(
    [param: Required, StringLength(200)] string Token,
    [param: Required, StringLength(256, MinimumLength = 10)] string Password);

/// <summary>What the studio gets back after signing in.</summary>
/// <param name="AccessToken">Bearer token for subsequent requests.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="UserId">The signed-in account.</param>
/// <param name="Email">The account's address.</param>
/// <param name="EmailVerified">Whether the address has been proven.</param>
public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string Email,
    bool EmailVerified);

/// <summary>What the caller looks like to the API.</summary>
/// <param name="UserId">The authenticated subject.</param>
/// <param name="Email">The subject's address, when the scheme carries one.</param>
/// <param name="Scheme">Which authentication scheme accepted the request.</param>
/// <param name="TokenId">The API token used, when one was.</param>
/// <param name="Org">The organisation an API token is confined to.</param>
public sealed record CallerResponse(
    string? UserId,
    string? Email,
    string? Scheme,
    string? TokenId,
    string? Org);

/// <summary>Sign-up, sign-in, refresh, and the emailed flows.</summary>
public static class AuthEndpoints
{
    /// <summary>Maps the authentication endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/auth").WithTags("Authentication");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .WithSummary("Create an account and send a verification link.");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithSummary("Exchange credentials for an access token and a refresh cookie.");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithSummary("Rotate the refresh cookie and issue a new access token.");

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithSummary("Revoke the current refresh family.");

        group.MapPost("/verify-email", VerifyEmailAsync)
            .AllowAnonymous()
            .WithSummary("Redeem an emailed verification token.");

        group.MapPost("/password/forgot", ForgotPasswordAsync)
            .AllowAnonymous()
            .WithSummary("Send a password reset link, if the address is known.");

        group.MapPost("/password/reset", ResetPasswordAsync)
            .AllowAnonymous()
            .WithSummary("Set a new password using an emailed token.");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithSummary("Describe the caller.");

        group.MapGet("/oauth/{provider}", StartOAuthAsync)
            .AllowAnonymous()
            .WithSummary("Begin sign-in through an external identity provider.");

        group.MapGet("/oauth/{provider}/complete", CompleteOAuthAsync)
            .AllowAnonymous()
            .WithSummary("Finish an external sign-in and hand back a Shellwright session.");

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] CredentialsRequest request,
        IdentityService identities,
        UserTokenService userTokens,
        IEmailSender email,
        RefreshCookie cookie,
        CancellationToken cancellationToken)
    {
        if (RejectWeakPassword(request) is { } problem)
        {
            return problem;
        }

        var user = await identities.RegisterAsync(request.Email, request.Password, cancellationToken);

        if (user is null)
        {
            // ⚠️ 202, and the same body as a success.
            //
            // Returning "that address is taken" here would turn registration
            // into an account-existence oracle for any address someone cares to
            // try — the exact leak the login endpoint goes to some trouble to
            // avoid. The person who really owns the address finds out by email;
            // the person guessing learns nothing.
            return TypedResults.StatusCode(StatusCodes.Status202Accepted);
        }

        var secret = await userTokens.IssueAsync(user.Id, UserTokenPurpose.EmailVerification, cancellationToken);
        await email.SendAsync(
            new EmailMessage(
                user.Email,
                "Verify your Shellwright address",
                $"Confirm your address: {cookie.StudioOrigin}/verify-email?token={secret}\n\nThe link is good for thirty minutes."),
            cancellationToken);

        return TypedResults.StatusCode(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] CredentialsRequest request,
        HttpResponse response,
        IdentityService identities,
        RefreshTokenService refreshTokens,
        AccessTokenIssuer accessTokens,
        RefreshCookie cookie,
        CancellationToken cancellationToken)
    {
        var (outcome, user) = await identities.SignInAsync(request.Email, request.Password, cancellationToken);

        if (outcome == SignInOutcome.LockedOut)
        {
            return TypedResults.Problem(
                title: "Too many attempts",
                detail: "This account is temporarily refusing sign-ins. Try again shortly.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (outcome != SignInOutcome.Success || user is null)
        {
            return TypedResults.Problem(
                title: "Invalid credentials",
                detail: "That email address and password do not match an account.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var refresh = await refreshTokens.IssueAsync(user.Id, cancellationToken);
        cookie.Write(response, refresh.Secret, refresh.ExpiresAt);

        var (token, expiresAt) = accessTokens.Issue(user.Id, user.Email);
        return TypedResults.Ok(new SessionResponse(
            token,
            expiresAt,
            user.Id,
            user.Email,
            user.EmailVerifiedAt is not null));
    }

    private static async Task<IResult> RefreshAsync(
        HttpRequest request,
        HttpResponse response,
        RefreshTokenService refreshTokens,
        AccessTokenIssuer accessTokens,
        RefreshCookie cookie,
        ShellwrightDbContext database,
        CancellationToken cancellationToken)
    {
        var presented = RefreshCookie.Read(request);

        if (presented is null)
        {
            return TypedResults.Problem(
                title: "No session",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var (result, failure) = await refreshTokens.RotateAsync(presented, cancellationToken);

        if (result is null)
        {
            cookie.Clear(response);

            // Every failure looks the same from outside, including reuse. The
            // attacker holding a replayed token must not be told that the
            // replay is what gave them away — the family is already revoked,
            // and saying so only tells them to move faster next time.
            return TypedResults.Problem(
                title: "Session expired",
                detail: "Sign in again.",
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["reason"] = failure.ToString() });
        }

        cookie.Write(response, result.Secret, result.ExpiresAt);

        var user = await database.Users
            .Where(x => x.Id == result.UserId)
            .Select(x => new { x.Id, x.Email, x.EmailVerifiedAt })
            .FirstAsync(cancellationToken);

        var (token, expiresAt) = accessTokens.Issue(user.Id, user.Email);
        return TypedResults.Ok(new SessionResponse(
            token,
            expiresAt,
            user.Id,
            user.Email,
            user.EmailVerifiedAt is not null));
    }

    private static async Task<IResult> LogoutAsync(
        HttpRequest request,
        HttpResponse response,
        RefreshTokenService refreshTokens,
        RefreshCookie cookie,
        CancellationToken cancellationToken)
    {
        if (RefreshCookie.Read(request) is { } presented)
        {
            await refreshTokens.SignOutAsync(presented, cancellationToken);
        }

        cookie.Clear(response);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> VerifyEmailAsync(
        [FromBody] TokenRequest request,
        UserTokenService userTokens,
        ShellwrightDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var userId = await userTokens.RedeemAsync(request.Token, UserTokenPurpose.EmailVerification, cancellationToken);

        if (userId is null)
        {
            return TypedResults.Problem(
                title: "Link expired",
                detail: "That verification link is no longer valid. Request another.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        await database.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.EmailVerifiedAt, clock.GetUtcNow()), cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] EmailRequest request,
        ShellwrightDbContext database,
        UserTokenService userTokens,
        IEmailSender email,
        RefreshCookie cookie,
        CancellationToken cancellationToken)
    {
        var normalised = IdentityService.NormaliseEmail(request.Email);

        var user = await database.Users
            .Where(x => x.Email == normalised)
            .Select(x => new { x.Id, x.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is not null)
        {
            var secret = await userTokens.IssueAsync(user.Id, UserTokenPurpose.PasswordReset, cancellationToken);
            await email.SendAsync(
                new EmailMessage(
                    user.Email,
                    "Reset your Shellwright password",
                    $"Set a new password: {cookie.StudioOrigin}/reset-password?token={secret}\n\nThe link is good for thirty minutes. If you did not ask for this, ignore it."),
                cancellationToken);
        }

        // 202 whether or not the address is known — same reasoning as
        // registration.
        return TypedResults.StatusCode(StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        UserTokenService userTokens,
        IdentityService identities,
        RefreshTokenService refreshTokens,
        ShellwrightDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.Password.Length < 10)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["A password must be at least 10 characters."],
            });
        }

        var userId = await userTokens.RedeemAsync(request.Token, UserTokenPurpose.PasswordReset, cancellationToken);

        if (userId is null)
        {
            return TypedResults.Problem(
                title: "Link expired",
                detail: "That reset link is no longer valid. Request another.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await database.Users.AsTracking().FirstAsync(x => x.Id == userId, cancellationToken);
        await identities.SetPasswordAsync(user, request.Password, cancellationToken);

        // ⚠️ Changing a password ends every session, not just this one. The
        // usual reason someone resets a password is that they believe somebody
        // else has it, and leaving the intruder's refresh token live would make
        // the reset theatre.
        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);

        return TypedResults.NoContent();
    }

    private static Ok<CallerResponse> Me(HttpContext context)
    {
        var identity = context.User;

        return TypedResults.Ok(new CallerResponse(
            identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            identity.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
            identity.Identity?.AuthenticationType,
            identity.FindFirst(ShellwrightClaims.TokenId)?.Value,
            identity.FindFirst(ShellwrightClaims.TokenOrg)?.Value));
    }

    private static async Task<IResult> StartOAuthAsync(
        string provider,
        IAuthenticationSchemeProvider schemes)
    {
        // Both checks matter. The first rejects a provider this build has never
        // heard of; the second rejects one it knows but this deployment has no
        // credentials for, which is otherwise a 500 deep inside the handler.
        var known = OAuthProviders.All.Any(x => x.Scheme == provider);
        var registered = known && await schemes.GetSchemeAsync(provider) is not null;

        return registered
            ? TypedResults.Challenge(
                new AuthenticationProperties { RedirectUri = $"/v1/auth/oauth/{provider}/complete" },
                [provider])
            : TypedResults.Problem(
                title: "Unknown provider",
                detail: $"'{provider}' is not configured on this deployment.",
                statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Turns the provider's ticket into a Shellwright session.
    /// </summary>
    /// <remarks>
    /// ⚠️ The handoff cookie is signed out before the redirect, not after and
    /// not never. It exists only because the OAuth handler must sign in to
    /// something; leaving it set would mean a second, weaker session cookie
    /// riding alongside the refresh cookie with none of its restrictions.
    /// </remarks>
    private static async Task<IResult> CompleteOAuthAsync(
        string provider,
        HttpContext context,
        ShellwrightDbContext database,
        RefreshTokenService refreshTokens,
        RefreshCookie cookie,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var result = await context.AuthenticateAsync(AuthSchemes.OAuthHandoff);

        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                title: "Sign-in did not complete",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var providerUserId = result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var address = result.Principal.FindFirst(ClaimTypes.Email)?.Value;

        await context.SignOutAsync(AuthSchemes.OAuthHandoff);

        if (string.IsNullOrWhiteSpace(providerUserId) || string.IsNullOrWhiteSpace(address))
        {
            // GitHub returns a null email when the address is private. There is
            // nothing useful to do about it here beyond saying so plainly:
            // creating an account without an address would produce one nobody
            // can recover.
            return TypedResults.Problem(
                title: "No address from provider",
                detail: "Make an email address visible to Shellwright at your provider, then try again.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await OAuthProviders.LinkAsync(
            database,
            provider,
            providerUserId,
            address,
            clock,
            cancellationToken);

        var refresh = await refreshTokens.IssueAsync(user.Id, cancellationToken);
        cookie.Write(context.Response, refresh.Secret, refresh.ExpiresAt);

        // The access token is not in the redirect. Putting it in a fragment or
        // a query string would write it into browser history, the referrer of
        // whatever loads next, and any proxy log in between; the studio calls
        // /v1/auth/refresh on load and gets one over a normal response body.
        return TypedResults.Redirect($"{cookie.StudioOrigin}/signed-in");
    }

    /// <summary>
    /// Rejects the two password mistakes worth rejecting at this layer.
    /// </summary>
    /// <remarks>
    /// Length is enforced by the annotation. This adds only the checks that
    /// need the rest of the request: a password identical to the address, and
    /// one that is entirely whitespace. Composition rules beyond that
    /// ("one number, one symbol") push people towards <c>Password1!</c> and are
    /// deliberately absent.
    /// </remarks>
    private static ValidationProblem? RejectWeakPassword(CredentialsRequest request)
    {
        var problems = new Dictionary<string, string[]>();

        if (string.Equals(request.Password.Trim(), request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            problems["password"] = ["A password cannot be your email address."];
        }
        else if (string.IsNullOrWhiteSpace(request.Password))
        {
            problems["password"] = ["A password cannot be only whitespace."];
        }

        return problems.Count > 0 ? TypedResults.ValidationProblem(problems) : null;
    }
}
