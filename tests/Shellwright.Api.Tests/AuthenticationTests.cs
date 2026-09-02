using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Auth;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>Sign-up, sign-in, rotation, and the emailed flows, over real HTTP.</summary>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class AuthenticationTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    /// <summary>TC-S06-API-007 — the happy path, end to end.</summary>
    [Fact]
    public async Task Register_then_login_issues_an_access_token_and_a_refresh_cookie()
    {
        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await response.Content.ReadFromJsonAsync<JsonElement>();
        session.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        session.GetProperty("email").GetString().Should().Be(email);
        session.GetProperty("emailVerified").GetBoolean().Should().BeFalse();

        RefreshSecret(response).Should().NotBeNullOrEmpty();
    }

    /// <summary>The refresh cookie is not reachable from script, and not sent to every endpoint.</summary>
    [Fact]
    public async Task Refresh_cookie_is_http_only_secure_and_path_scoped()
    {
        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        var cookie = SetCookieHeader(response);

        cookie.Should().Contain("httponly", Exactly.Once());
        cookie.Should().Contain("secure");
        cookie.Should().Contain("path=/v1/auth");
        cookie.Should().Contain("samesite=lax");
    }

    /// <summary>An access token opens an authenticated endpoint.</summary>
    [Fact]
    public async Task Access_token_authenticates_a_request()
    {
        var client = factory.CreateApiClient();
        var token = await SignInAsync(client);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var response = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var caller = await response.Content.ReadFromJsonAsync<JsonElement>();
        caller.GetProperty("scheme").GetString().Should().Be(AuthSchemes.AccessToken);
    }

    /// <summary>An expired access token does not.</summary>
    [Fact]
    public async Task Access_token_stops_working_when_it_expires()
    {
        var client = factory.CreateApiClient();
        var token = await SignInAsync(client);

        factory.Clock.Advance(TimeSpan.FromMinutes(16));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var response = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Rotation issues a new secret and retires the old one.</summary>
    [Fact]
    public async Task Refresh_rotates_the_cookie()
    {
        var client = factory.CreateApiClient();
        var token = await SignInAsync(client);

        var response = await PostWithRefreshAsync(client, "/v1/auth/refresh", token.RefreshSecret);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        RefreshSecret(response).Should().NotBe(token.RefreshSecret);
    }

    /// <summary>
    /// TC-S06-SEC-003 — replaying a spent refresh token revokes the whole
    /// family and is recorded.
    /// </summary>
    /// <remarks>
    /// ⚠️ The third assertion is the one that matters. Rotation on its own is
    /// not detection: without revoking the family, the attacker who replayed
    /// the token simply keeps the session and the real user's next refresh
    /// looks like an ordinary expiry.
    /// </remarks>
    [Fact]
    public async Task Replaying_a_spent_refresh_token_revokes_the_family()
    {
        var client = factory.CreateApiClient();
        var token = await SignInAsync(client);

        var first = await PostWithRefreshAsync(client, "/v1/auth/refresh", token.RefreshSecret);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var successor = RefreshSecret(first)!;

        var replay = await PostWithRefreshAsync(client, "/v1/auth/refresh", token.RefreshSecret);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The successor the legitimate client is holding dies too. Signing
        // everybody out is the intended outcome, not collateral damage.
        var afterRevocation = await PostWithRefreshAsync(client, "/v1/auth/refresh", successor);
        afterRevocation.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var context = fixture.CreateContext(token.UserId);
        await using (context.ConfigureAwait(false))
        {
            var live = await context.RefreshTokens
                .CountAsync(x => x.UserId == token.UserId && x.RevokedAt == null);
            live.Should().Be(0);
        }

        (await SecurityEventKindsAsync(token.UserId)).Should().Contain("refresh.reuse_detected");
    }

    /// <summary>Signing out revokes the family rather than just dropping the cookie.</summary>
    [Fact]
    public async Task Logout_revokes_the_family()
    {
        var client = factory.CreateApiClient();
        var token = await SignInAsync(client);

        var logout = await PostWithRefreshAsync(client, "/v1/auth/logout", token.RefreshSecret);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await PostWithRefreshAsync(client, "/v1/auth/refresh", token.RefreshSecret);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Rotation does not extend the session past its original expiry.</summary>
    [Fact]
    public async Task Rotation_does_not_extend_the_absolute_lifetime()
    {
        var client = factory.CreateApiClient();
        var token = await SignInAsync(client);

        var secret = token.RefreshSecret;

        for (var day = 0; day < 3; day++)
        {
            factory.Clock.Advance(TimeSpan.FromDays(10));
            var response = await PostWithRefreshAsync(client, "/v1/auth/refresh", secret);

            if (day < 2)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                secret = RefreshSecret(response)!;
            }
            else
            {
                // Thirty days after signing in, you sign in again — however
                // many times the token was rotated in between.
                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
        }
    }

    /// <summary>An unknown address and a wrong password are the same answer.</summary>
    [Fact]
    public async Task Unknown_address_and_wrong_password_are_indistinguishable()
    {
        var client = factory.CreateApiClient();
        var (email, _) = await ApiFactory.RegisterAsync(client);

        var wrongPassword = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { email, password = "not the right password" });

        var unknownAccount = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { email = $"nobody-{Guid.NewGuid():N}@example.test", password = "not the right password" });

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Compared with the per-request identifiers removed. Those differ by
        // design — they are how support finds one request among millions — and
        // they carry nothing about whether the account exists.
        var first = await StableBodyAsync(wrongPassword);
        var second = await StableBodyAsync(unknownAccount);
        second.Should().Be(first, "a different body would answer 'does this account exist'");
    }

    /// <summary>TC-S06-SEC-004 — repeated failures back the account off.</summary>
    [Fact]
    public async Task Repeated_failures_lock_the_account_then_release_it()
    {
        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync("/v1/auth/login", new { email, password = "wrong" });
        }

        var lockedOut = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        lockedOut.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // ⚠️ And it must let go. A backoff with no ceiling is a denial of
        // service anybody can aim at any address they know.
        factory.Clock.Advance(TimeSpan.FromMinutes(20));

        var afterBackoff = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        afterBackoff.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Registering an address that already exists says nothing about it.</summary>
    [Fact]
    public async Task Registering_a_taken_address_does_not_say_so()
    {
        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        var second = await client.PostAsJsonAsync("/v1/auth/register", new { email, password });

        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.Messages.Count(x => x.To == email).Should().Be(1, "the second attempt must not send mail");
    }

    /// <summary>Verification is single-use and time-limited.</summary>
    [Fact]
    public async Task Email_verification_works_once()
    {
        var client = factory.CreateApiClient();
        var (email, _) = await ApiFactory.RegisterAsync(client);

        var token = TokenFromLink(factory.Email.Last(email)!.Body);

        var first = await client.PostAsJsonAsync("/v1/auth/verify-email", new { token });
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await client.PostAsJsonAsync("/v1/auth/verify-email", new { token });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>A verification token cannot be spent as a password reset.</summary>
    [Fact]
    public async Task A_verification_token_cannot_reset_a_password()
    {
        var client = factory.CreateApiClient();
        var (email, _) = await ApiFactory.RegisterAsync(client);

        var token = TokenFromLink(factory.Email.Last(email)!.Body);

        var response = await client.PostAsJsonAsync(
            "/v1/auth/password/reset",
            new { token, password = "a completely new password" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Reset links expire.</summary>
    [Fact]
    public async Task A_reset_link_expires_after_thirty_minutes()
    {
        var client = factory.CreateApiClient();
        var (email, _) = await ApiFactory.RegisterAsync(client);

        await client.PostAsJsonAsync("/v1/auth/password/forgot", new { email });
        var token = TokenFromLink(factory.Email.Last(email)!.Body);

        factory.Clock.Advance(TimeSpan.FromMinutes(31));

        var response = await client.PostAsJsonAsync(
            "/v1/auth/password/reset",
            new { token, password = "a completely new password" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Asking for a reset invalidates the link sent before it.</summary>
    [Fact]
    public async Task Requesting_a_second_reset_link_invalidates_the_first()
    {
        var client = factory.CreateApiClient();
        var (email, _) = await ApiFactory.RegisterAsync(client);

        await client.PostAsJsonAsync("/v1/auth/password/forgot", new { email });
        var first = TokenFromLink(factory.Email.Last(email)!.Body);

        await client.PostAsJsonAsync("/v1/auth/password/forgot", new { email });
        var second = TokenFromLink(factory.Email.Last(email)!.Body);

        first.Should().NotBe(second);

        var stale = await client.PostAsJsonAsync(
            "/v1/auth/password/reset",
            new { token = first, password = "a completely new password" });

        stale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Resetting a password ends every session, including the intruder's.</summary>
    [Fact]
    public async Task Resetting_a_password_revokes_every_session()
    {
        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);
        var session = await SignInAsync(client, email, password);

        await client.PostAsJsonAsync("/v1/auth/password/forgot", new { email });
        var token = TokenFromLink(factory.Email.Last(email)!.Body);

        var reset = await client.PostAsJsonAsync(
            "/v1/auth/password/reset",
            new { token, password = "a completely new password" });
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await PostWithRefreshAsync(client, "/v1/auth/refresh", session.RefreshSecret);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A forgotten-password request for an unknown address behaves identically.</summary>
    [Fact]
    public async Task Forgotten_password_for_an_unknown_address_says_nothing()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/password/forgot",
            new { email = $"nobody-{Guid.NewGuid():N}@example.test" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.Messages.Should().BeEmpty();
    }

    /// <summary>An unconfigured provider is a 404, not a 500 from inside the handler.</summary>
    [Fact]
    public async Task An_unconfigured_oauth_provider_is_not_found()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri("/v1/auth/oauth/github", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record Session(string AccessToken, string RefreshSecret, Guid UserId);

    /// <summary>The response body with the fields that legitimately vary removed.</summary>
    private static async Task<string> StableBodyAsync(HttpResponseMessage response)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            await response.Content.ReadAsStringAsync())!;

        body.Remove("traceId");
        body.Remove("correlationId");

        return JsonSerializer.Serialize(body.OrderBy(x => x.Key, StringComparer.Ordinal));
    }

    private static async Task<Session> SignInAsync(HttpClient client, string? email = null, string? password = null)
    {
        if (email is null || password is null)
        {
            (email, password) = await ApiFactory.RegisterAsync(client);
        }

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new Session(
            body.GetProperty("accessToken").GetString()!,
            RefreshSecret(response)!,
            body.GetProperty("userId").GetGuid());
    }

    private static async Task<HttpResponseMessage> PostWithRefreshAsync(
        HttpClient client,
        string path,
        string secret)
    {
        // The cookie is carried by hand rather than by a cookie container,
        // because the container refuses to store a Secure cookie received over
        // the test server's plain http. Setting Secure=false for tests would
        // mean the property under test is not the one that ships.
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Cookie", $"{RefreshCookie.Name}={secret}");
        return await client.SendAsync(request);
    }

    /// <summary>
    /// The Set-Cookie header, casefolded so attribute assertions are not
    /// hostage to how the framework capitalises HttpOnly this month.
    /// </summary>
    private static string SetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
#pragma warning disable CA1308 // Comparing attribute names, not normalising a security-relevant identifier.
            ? string.Join("; ", values).ToLowerInvariant()
#pragma warning restore CA1308
            : string.Empty;

    private static string? RefreshSecret(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (!value.StartsWith(RefreshCookie.Name + "=", StringComparison.Ordinal))
            {
                continue;
            }

            var secret = value[(RefreshCookie.Name.Length + 1)..].Split(';')[0];
            return string.IsNullOrEmpty(secret) ? null : secret;
        }

        return null;
    }

    private static string TokenFromLink(string body)
    {
        var marker = "token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = body.IndexOfAny(['\n', '\r', ' '], start);
        return end < 0 ? body[start..] : body[start..end];
    }

    private async Task<List<string>> SecurityEventKindsAsync(Guid userId)
    {
        // Read as the schema owner: the application role is granted INSERT on
        // this table and nothing else, which is the property being relied on.
        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var command = new Npgsql.NpgsqlCommand(
                "SELECT kind FROM security_events WHERE user_id = @user",
                connection);

            await using (command.ConfigureAwait(false))
            {
                command.Parameters.AddWithValue("user", userId);

                var kinds = new List<string>();
                var reader = await command.ExecuteReaderAsync();
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync())
                    {
                        kinds.Add(reader.GetString(0));
                    }
                }

                return kinds;
            }
        }
    }
}
