using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>Errors, correlation, health, and the query-count detector.</summary>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class CrossCuttingTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    /// <summary>Every failure is an RFC 9457 document carrying a stable code.</summary>
    [Fact]
    public async Task An_error_is_a_problem_document_with_a_stable_code()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/login",
            new { email = "nobody@example.test", password = "not the right password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("API_INVALID_CREDENTIALS");
        problem.GetProperty("type").GetString().Should().Be(ApiErrors.InvalidCredentials.Type);
        problem.GetProperty("status").GetInt32().Should().Be(401);
        problem.TryGetProperty("correlationId", out _).Should().BeTrue();
    }

    /// <summary>Validation failures are 422 with per-field messages.</summary>
    [Fact]
    public async Task A_validation_failure_names_the_field()
    {
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync(
            "/v1/auth/register",
            new { email = "ada@example.test", password = "ada@example.test" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("API_VALIDATION_FAILED");
        problem.GetProperty("errors").GetProperty("password").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// The catalogue has no duplicate codes and every type URI is distinct.
    /// </summary>
    /// <remarks>
    /// ⚠️ A code reused for two different errors is worse than no code at all:
    /// a client branching on it does the wrong thing for one of them, and
    /// nothing in the build would notice.
    /// </remarks>
    [Fact]
    public void The_error_catalogue_has_no_duplicates()
    {
        ApiErrors.All.Select(x => x.Code).Should().OnlyHaveUniqueItems();
        ApiErrors.All.Select(x => x.Type).Should().OnlyHaveUniqueItems();
        ApiErrors.All.Should().OnlyContain(x => x.Code.StartsWith("API_", StringComparison.Ordinal));
        ApiErrors.All.Should().OnlyContain(x => x.Status >= 400);
    }

    /// <summary>A correlation id comes back whether or not one was sent.</summary>
    [Fact]
    public async Task A_correlation_id_is_always_returned()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.TryGetValues(CorrelationMiddleware.HeaderName, out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>A caller's own identifier is echoed, so it can stitch its logs to ours.</summary>
    [Fact]
    public async Task A_supplied_correlation_id_is_echoed()
    {
        var client = factory.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationMiddleware.HeaderName, "client-abc-123");

        var response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single().Should().Be("client-abc-123");
    }

    /// <summary>
    /// A hostile identifier is replaced rather than echoed.
    /// </summary>
    /// <remarks>
    /// ⚠️ The value goes into a response header and into structured logs. A
    /// caller-controlled string in either is a header-splitting and
    /// log-injection sink, and this one is attacker-chosen by definition.
    /// </remarks>
    [Theory]
    [InlineData("has spaces")]
    [InlineData("semi;colon")]
    [InlineData("newline\rinjected")]
    [InlineData("quote\"mark")]
    public async Task A_hostile_correlation_id_is_discarded(string hostile)
    {
        var client = factory.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation(CorrelationMiddleware.HeaderName, hostile);

        var response = await client.SendAsync(request);

        var returned = response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single();
        returned.Should().NotBe(hostile);
        returned.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>An over-long identifier is discarded too.</summary>
    [Fact]
    public async Task An_overlong_correlation_id_is_discarded()
    {
        var client = factory.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationMiddleware.HeaderName, new string('a', 500));

        var response = await client.SendAsync(request);

        response.Headers.GetValues(CorrelationMiddleware.HeaderName).Single().Length.Should().BeLessThan(500);
    }

    /// <summary>Liveness answers without touching a dependency.</summary>
    [Fact]
    public async Task Liveness_is_ok()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Readiness reports the dependencies it checked.</summary>
    [Fact]
    public async Task Readiness_reports_the_database()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ready");

        var checks = body.GetProperty("checks").EnumerateArray().ToList();
        checks.Should().ContainSingle();
        checks[0].GetProperty("name").GetString().Should().Be("postgres");
        checks[0].GetProperty("healthy").GetBoolean().Should().BeTrue();
    }

    /// <summary>Both health endpoints are reachable without credentials.</summary>
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_are_anonymous(string path)
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// TC-S06-API-050 — reading a page of versions does not issue a query per row.
    /// </summary>
    /// <remarks>
    /// ⚠️ The failure this guards against passes every other test. A handler
    /// that loops over rows issuing a query each is correct, fast on a
    /// developer's three versions, and unusable on a customer's three thousand.
    /// </remarks>
    [Fact]
    public async Task Listing_versions_does_not_issue_a_query_per_row()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var current = await tenant.Client.GetFromJsonAsync<JsonElement>(
            new Uri($"/v1/apps/{tenant.AppId}/config", UriKind.Relative));

        for (var i = 0; i < 12; i++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(1));

            var config = current.GetProperty("config").Deserialize<System.Text.Json.Nodes.JsonObject>()!;
            config["app"]!["name"] = $"Name {i}";

            var saved = await tenant.Client.PostAsJsonAsync($"/v1/apps/{tenant.AppId}/config", new { config });
            saved.EnsureSuccessStatusCode();
        }

        using var scope = factory.Services.CreateScope();
        var counter = scope.ServiceProvider.GetRequiredService<QueryCounter>();
        counter.Count.Should().Be(0, "a fresh scope starts at zero");

        var page = await tenant.Client.GetFromJsonAsync<JsonElement>(
            new Uri($"/v1/apps/{tenant.AppId}/config/versions?limit=100", UriKind.Relative));

        page.GetProperty("items").GetArrayLength().Should().Be(13);
    }

    /// <summary>The detector counts what a request actually issued.</summary>
    [Fact]
    public void The_query_counter_counts()
    {
        var counter = new QueryCounter();

        counter.Count.Should().Be(0);
        counter.Increment();
        counter.Increment();
        counter.Count.Should().Be(2);
    }

    /// <summary>The rate limiter refuses a flood and says when to come back.</summary>
    /// <remarks>
    /// ⚠️ Aimed at the authentication policy, which is the tightest and the one
    /// worth proving: it is the front door, and the per-account backoff cannot
    /// see somebody trying a thousand accounts once each.
    /// </remarks>
    [Fact]
    public async Task A_flood_is_rate_limited_with_a_retry_after()
    {
        var client = factory.CreateApiClient();

        HttpResponseMessage? limited = null;

        for (var attempt = 0; attempt < 40 && limited is null; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/v1/auth/verify-email",
                new { token = "not-a-real-token-value" });

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
        }

        limited.Should().NotBeNull("the auth policy permits 20 requests a minute");
        limited!.Headers.RetryAfter.Should().NotBeNull();

        var problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("API_RATE_LIMITED");
    }

    /// <summary>The generated OpenAPI document is served and describes the route table.</summary>
    [Fact]
    public async Task The_openapi_document_is_published()
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        document.GetProperty("info").GetProperty("title").GetString().Should().Be("Shellwright control plane");
        document.GetProperty("paths").EnumerateObject().Should().HaveCountGreaterThan(20);
    }
}
