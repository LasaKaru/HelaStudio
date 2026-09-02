using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// TC-S07-API-001–016 — starting, watching, and cancelling builds.
/// </summary>
/// <remarks>
/// ⚠️ Against the real application and the real database, with only the
/// workflow client substituted. What this endpoint is responsible for is
/// deciding <i>whether</i> to start a build — the required idempotency key, the
/// concurrency limit, the authorisation — and none of that becomes truer for
/// having a Temporal server behind it. Whether the workflow then runs correctly
/// is the orchestrator's business, and its tests use a real one.
/// </remarks>
/// <param name="fixture">The database fixture.</param>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class BuildApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    [Fact(DisplayName = "A build is accepted and the workflow is started")]
    public async Task StartsABuild()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var response = await StartAsync(tenant, key: "first");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("state").GetString().Should().Be(nameof(BuildState.Queued));
        body.GetProperty("appId").GetGuid().Should().Be(tenant.AppId);

        var started = factory.Workflows.Started.Should().ContainSingle().Subject;
        started.Id.Should().Be(body.GetProperty("id").GetGuid());

        // ⚠️ The row is written before the workflow starts. A workflow with no
        // row is a build nobody can see, cancel or bill; a row with no workflow
        // is a build stuck in Queued, which is visible and recoverable.
        started.WorkflowId.Should().Be($"build-{started.Id}");
    }

    [Fact(DisplayName = "A build without an Idempotency-Key is refused")]
    public async Task RequiresAnIdempotencyKey()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildsUrl(tenant))
        {
            Content = JsonContent.Create(new { platform = "Android", type = "Debug" }),
        };

        var response = await tenant.Client.SendAsync(request);

        // ⚠️ Required here and optional everywhere else. A retried save costs a
        // duplicate row the content address collapses anyway; a retried build
        // costs runner minutes somebody is billed for, and the server has no
        // other way to tell "start another" from "I did not hear you".
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("API_IDEMPOTENCY_KEY_REQUIRED");

        factory.Workflows.Started.Should().BeEmpty("nothing may have been started");
    }

    [Fact(DisplayName = "The same key twice returns the same build and starts one workflow")]
    public async Task IsIdempotent()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var first = await StartAsync(tenant, key: "same");
        var second = await StartAsync(tenant, key: "same");

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        secondId.Should().Be(firstId);
        factory.Workflows.Started.Should().ContainSingle("a retry must not start a second build");
    }

    [Fact(DisplayName = "Concurrent identical requests still start exactly one build")]
    public async Task ConcurrentRetriesStartOneBuild()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        // ⚠️ The race the unique index exists for: both requests read, both
        // find nothing, both insert. Without the index they both succeed and
        // the customer pays twice for one click.
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => StartAsync(tenant, key: "raced")));

        responses.Should().OnlyContain(x =>
            x.StatusCode == HttpStatusCode.Accepted || x.StatusCode == HttpStatusCode.OK);

        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            ids.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        ids.Distinct().Should().ContainSingle("every caller must be told about the same build");
        factory.Workflows.Started.Should().ContainSingle();
    }

    [Fact(DisplayName = "An organisation at its concurrency limit is refused, not queued indefinitely")]
    public async Task EnforcesConcurrency()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        // The default limit is two.
        (await StartAsync(tenant, key: "a")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await StartAsync(tenant, key: "b")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var third = await StartAsync(tenant, key: "c");

        // ⚠️ Per organisation, so one customer's runaway CI loop cannot consume
        // the fleet and leave everybody else merely observing that the service
        // is slow.
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var problem = await third.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("API_BUILD_CONCURRENCY_EXCEEDED");
    }

    [Fact(DisplayName = "A build can be read back and appears in the listing")]
    public async Task ReadsAndLists()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var started = await StartAsync(tenant, key: "read");
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var one = await tenant.Client.GetFromJsonAsync<JsonElement>($"{BuildsUrl(tenant)}/{id}");
        one.GetProperty("id").GetGuid().Should().Be(id);

        var page = await tenant.Client.GetFromJsonAsync<JsonElement>(BuildsUrl(tenant));
        page.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .Should().Contain(id);
    }

    [Fact(DisplayName = "Another tenant cannot see or cancel this tenant's build")]
    public async Task IsolatedFromOtherTenants()
    {
        var alpha = await TenantBuilder.CreateAsync(factory);
        var beta = await TenantBuilder.CreateAsync(factory);

        var started = await StartAsync(alpha, key: "alpha");
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Beta asks about alpha's app: a 404, because the app is invisible.
        (await beta.Client.GetAsync(new Uri($"{BuildsUrl(alpha)}/{id}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await beta.Client.PostAsync(new Uri($"{BuildsUrl(alpha)}/{id}/cancel", UriKind.Relative), content: null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        factory.Workflows.Cancelled.Should().BeEmpty();
    }

    [Fact(DisplayName = "Cancelling asks the workflow to stop, and does not write the state itself")]
    public async Task CancelsThroughTheWorkflow()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var started = await StartAsync(tenant, key: "cancel");
        var body = await started.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();

        var response = await tenant.Client.PostAsync(new Uri($"{BuildsUrl(tenant)}/{id}/cancel", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Workflows.Cancelled.Should().ContainSingle().Which.Should().Be($"build-{id}");

        // ⚠️ Still Queued. Temporal owns whether the workflow actually stopped,
        // and the transition is recorded by the activity that runs when it
        // does. Writing Cancelled optimistically would let the row say
        // "stopped" while a runner kept burning metered minutes.
        var after = await tenant.Client.GetFromJsonAsync<JsonElement>($"{BuildsUrl(tenant)}/{id}");
        after.GetProperty("state").GetString().Should().Be(nameof(BuildState.Queued));
    }

    [Fact(DisplayName = "A build with no artifact has nothing to download")]
    public async Task NoArtifactYet()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var started = await StartAsync(tenant, key: "artifact");
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await tenant.Client.GetAsync(new Uri($"{BuildsUrl(tenant)}/{id}/artifact", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString().Should().Be("API_NO_ARTIFACT");
    }

    [Fact(DisplayName = "A build against another app's configuration version is refused")]
    public async Task RefusesAForeignConfigurationVersion()
    {
        var alpha = await TenantBuilder.CreateAsync(factory);
        var beta = await TenantBuilder.CreateAsync(factory);

        var betaConfig = await beta.Client.GetFromJsonAsync<JsonElement>($"/v1/apps/{beta.AppId}/config");
        var betaVersionId = betaConfig.GetProperty("version").GetProperty("id").GetGuid();

        var response = await StartAsync(alpha, key: "foreign", configVersionId: betaVersionId);

        // ⚠️ Row-level security already hides beta's version from alpha, so the
        // observable answer would be a 404 either way. The check is written
        // here anyway, because "the database happened to hide it" is not a
        // reason a reviewer can find, and the next query might not be scoped.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.Workflows.Started.Should().BeEmpty();
    }

    [Fact(DisplayName = "A viewer may watch builds but not start one")]
    public async Task ViewersCannotBuild()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var viewer = await TenantBuilder.AddMemberAsync(factory, tenant, OrgRole.Viewer);

        (await viewer.GetAsync(new Uri(BuildsUrl(tenant), UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildsUrl(tenant))
        {
            Content = JsonContent.Create(new { platform = "Android", type = "Debug" }),
        };
        request.Headers.Add("Idempotency-Key", "viewer");

        (await viewer.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        factory.Workflows.Started.Should().BeEmpty();
    }

    private static string BuildsUrl(TenantClient tenant) => $"/v1/apps/{tenant.AppId}/builds";

    private static async Task<HttpResponseMessage> StartAsync(
        TenantClient tenant,
        string key,
        Guid? configVersionId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildsUrl(tenant))
        {
            Content = JsonContent.Create(new
            {
                platform = "Android",
                type = "Debug",
                configVersionId,
            }),
        };

        request.Headers.Add("Idempotency-Key", key);

        return await tenant.Client.SendAsync(request);
    }
}
