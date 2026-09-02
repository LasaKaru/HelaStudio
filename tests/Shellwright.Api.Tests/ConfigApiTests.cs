using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>Saving, reading, listing, and comparing configurations.</summary>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class ConfigApiTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    /// <summary>A new app already has a configuration, so nothing has to special-case null.</summary>
    [Fact]
    public async Task A_new_app_has_a_current_configuration()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var response = await tenant.Client.GetAsync(Url(Config(tenant)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("config").GetProperty("app").GetProperty("name").GetString().Should().Be("Acme");
        body.GetProperty("version").GetProperty("codeKey").GetString().Should().HaveLength(64);
    }

    /// <summary>TC-S06-API-030 — saving an identical configuration is a no-op.</summary>
    [Fact]
    public async Task Saving_an_identical_configuration_returns_the_existing_version()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var current = await CurrentAsync(tenant);

        var config = current.GetProperty("config");
        var versionId = current.GetProperty("version").GetProperty("id").GetGuid();

        var response = await tenant.Client.PostAsJsonAsync(Config(tenant), new { config });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        saved.GetProperty("created").GetBoolean().Should().BeFalse();
        saved.GetProperty("version").GetProperty("id").GetGuid().Should().Be(versionId);

        (await VersionCountAsync(tenant)).Should().Be(1, "no new row may be written");
    }

    /// <summary>A different configuration is a new version, and becomes current.</summary>
    [Fact]
    public async Task Saving_a_changed_configuration_creates_a_version()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var config = Mutate(await CurrentAsync(tenant), "Acme Orders");

        var response = await tenant.Client.PostAsJsonAsync(Config(tenant), new { config, message = "Rename" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        saved.GetProperty("created").GetBoolean().Should().BeTrue();
        saved.GetProperty("version").GetProperty("message").GetString().Should().Be("Rename");

        var current = await CurrentAsync(tenant);
        current.GetProperty("config").GetProperty("app").GetProperty("name").GetString().Should().Be("Acme Orders");
        (await VersionCountAsync(tenant)).Should().Be(2);
    }

    /// <summary>
    /// TC-S06-API-031 — every diagnostic comes back, not the first one.
    /// </summary>
    /// <remarks>
    /// ⚠️ Fixing a configuration one error per round trip is a miserable
    /// experience, and worse, it hides how much work is left.
    ///
    /// The document below is schema-valid and semantically wrong in three
    /// independent places, which is deliberate — see
    /// <see cref="A_schema_violation_suppresses_the_semantic_rules"/> for why a
    /// document with a schema error would not have proved this.
    /// </remarks>
    [Fact]
    public async Task An_invalid_configuration_reports_every_error_at_once()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var config = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["app"] = new JsonObject
            {
                ["name"] = "Acme",
                ["bundleId"] = "com.acme.orders",
                ["initialUrl"] = "https://app.acme.com/",
                ["allowedOrigins"] = new JsonArray("https://app.acme.com"),
            },
            ["navigation"] = new JsonObject
            {
                ["drawer"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["items"] = new JsonArray(
                        new JsonObject { ["id"] = "home", ["label"] = "Home", ["url"] = "/" },
                        new JsonObject { ["id"] = "home", ["label"] = "Orders", ["url"] = "/orders" }),
                },
            },
            ["linkRules"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "catastrophic",
                    ["pattern"] = "^(a+)+$",
                    ["action"] = "internal",
                },
                new JsonObject
                {
                    ["id"] = "fallback",
                    ["pattern"] = ".*",
                    ["action"] = "externalBrowser",
                }),
            ["webOverrides"] = new JsonObject
            {
                ["headers"] = new JsonObject { ["Authorization"] = "Bearer 9f2c41ab7de05613" },
            },
        };

        var response = await tenant.Client.PostAsJsonAsync(Config(tenant), new { config });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var codes = problem.GetProperty("errors")
            .EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())
            .ToList();

        codes.Should().Contain("CFG_REGEX_CATASTROPHIC");
        codes.Should().Contain("CFG_DUPLICATE_ITEM_ID");
        codes.Should().Contain("CFG_SECRET_IN_CONFIG");
    }

    /// <summary>
    /// A schema violation deliberately suppresses the semantic rules.
    /// </summary>
    /// <remarks>
    /// ⚠️ Written down because it is surprising, and because the surprise cost
    /// a debugging session: a test that expected a catastrophic-regex finding
    /// alongside a malformed bundle id got only the schema errors, and the
    /// first reading was that the API had dropped diagnostics.
    ///
    /// It has not. Running semantic rules over a document of the wrong shape
    /// produces a cascade of secondary complaints about fields that are missing
    /// because the document never parsed properly, and the real error gets lost
    /// in them. Shape first, meaning second.
    /// </remarks>
    [Fact]
    public async Task A_schema_violation_suppresses_the_semantic_rules()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var config = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["app"] = new JsonObject
            {
                ["name"] = "Acme",
                ["bundleId"] = "not a bundle id",
                ["initialUrl"] = "https://app.acme.com/",
                ["allowedOrigins"] = new JsonArray("https://app.acme.com"),
            },
            ["linkRules"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = "catastrophic",
                    ["pattern"] = "^(a+)+$",
                    ["action"] = "internal",
                }),
        };

        var response = await tenant.Client.PostAsJsonAsync(Config(tenant), new { config });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);

        var codes = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("errors")
            .EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())
            .ToList();

        codes.Should().Contain("CFG_BUNDLE_ID_INVALID");
        codes.Should().NotContain("CFG_REGEX_CATASTROPHIC");
    }

    /// <summary>Warnings are surfaced on a successful save rather than swallowed.</summary>
    [Fact]
    public async Task A_successful_save_still_reports_its_warnings()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var current = await CurrentAsync(tenant);

        var config = current.GetProperty("config").Deserialize<JsonObject>()!;
        config["permissions"] = new JsonObject { ["camera"] = true };

        var response = await tenant.Client.PostAsJsonAsync(Config(tenant), new { config });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        saved.GetProperty("warnings").GetArrayLength().Should().BeGreaterThan(0);
    }

    /// <summary>Validation answers the question without writing anything.</summary>
    [Fact]
    public async Task Validate_reports_without_saving()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var config = Mutate(await CurrentAsync(tenant), "Renamed");

        var response = await tenant.Client.PostAsJsonAsync($"{Config(tenant)}/validate", new { config });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valid").GetBoolean().Should().BeTrue();

        (await VersionCountAsync(tenant)).Should().Be(1, "validation must not write");
    }

    /// <summary>An invalid document is still a 200 from validate — it answered the question.</summary>
    [Fact]
    public async Task Validate_returns_200_for_an_invalid_document()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var response = await tenant.Client.PostAsJsonAsync(
            $"{Config(tenant)}/validate",
            new { config = new JsonObject { ["schemaVersion"] = 1 } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("valid").GetBoolean().Should().BeFalse();
        body.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    /// <summary>A viewer may validate and may not save.</summary>
    [Fact]
    public async Task A_viewer_may_validate_but_not_save()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var viewer = await TenantBuilder.AddMemberAsync(factory, tenant, OrgRole.Viewer);

        var config = Mutate(await CurrentAsync(tenant), "Viewer Rename");

        var validate = await viewer.PostAsJsonAsync($"{Config(tenant)}/validate", new { config });
        validate.StatusCode.Should().Be(HttpStatusCode.OK);

        var save = await viewer.PostAsJsonAsync(Config(tenant), new { config });
        save.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>TC-S06-API-038 — a matching entity tag returns 304 with no body.</summary>
    [Fact]
    public async Task A_matching_etag_returns_not_modified()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var first = await tenant.Client.GetAsync(Url(Config(tenant)));
        var etag = first.Headers.ETag!.ToString();

        using var conditional = new HttpRequestMessage(HttpMethod.Get, Config(tenant));
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var second = await tenant.Client.SendAsync(conditional);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The tag changes when the content does, and only then.
    /// </summary>
    /// <remarks>
    /// The version id is content-addressed, so a save that changes nothing
    /// leaves the tag alone — which is the case the studio's polling makes most
    /// often.
    /// </remarks>
    [Fact]
    public async Task The_etag_tracks_content_rather_than_writes()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var before = (await tenant.Client.GetAsync(Url(Config(tenant)))).Headers.ETag!.ToString();

        var current = await CurrentAsync(tenant);
        await tenant.Client.PostAsJsonAsync(Config(tenant), new { config = current.GetProperty("config") });

        var unchanged = (await tenant.Client.GetAsync(Url(Config(tenant)))).Headers.ETag!.ToString();
        unchanged.Should().Be(before);

        await tenant.Client.PostAsJsonAsync(Config(tenant), new { config = Mutate(current, "Different") });

        var changed = (await tenant.Client.GetAsync(Url(Config(tenant)))).Headers.ETag!.ToString();
        changed.Should().NotBe(before);
    }

    /// <summary>Versions page newest-first without repeating or skipping rows.</summary>
    [Fact]
    public async Task Versions_page_through_without_gaps_or_repeats()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var current = await CurrentAsync(tenant);

        for (var i = 0; i < 6; i++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            var saved = await tenant.Client.PostAsJsonAsync(
                Config(tenant),
                new { config = Mutate(current, $"Name {i}") });
            saved.EnsureSuccessStatusCode();
        }

        var seen = new List<Guid>();
        string? cursor = null;

        do
        {
            var url = $"{Config(tenant)}/versions?limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            var page = await tenant.Client.GetFromJsonAsync<JsonElement>(url);

            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));

            cursor = page.TryGetProperty("nextCursor", out var next) && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
        }
        while (cursor is not null);

        seen.Should().HaveCount(7, "one seeded version plus six saves");
        seen.Should().OnlyHaveUniqueItems();
    }

    /// <summary>A cursor this endpoint did not issue is refused rather than ignored.</summary>
    [Fact]
    public async Task A_forged_cursor_is_refused()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var response = await tenant.Client.GetAsync(Url($"{Config(tenant)}/versions?cursor=not-a-cursor"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>A diff names what changed and nothing else.</summary>
    [Fact]
    public async Task A_diff_names_only_what_changed()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var current = await CurrentAsync(tenant);
        var from = current.GetProperty("version").GetProperty("id").GetGuid();

        var saved = await tenant.Client.PostAsJsonAsync(
            Config(tenant),
            new { config = Mutate(current, "Acme Two") });
        var to = (await saved.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetProperty("id").GetGuid();

        var diff = await tenant.Client.GetFromJsonAsync<JsonElement>(
            $"{Config(tenant)}/diff?from={from}&to={to}");

        var changes = diff.GetProperty("changes").EnumerateArray().ToList();

        changes.Should().ContainSingle();
        changes[0].GetProperty("path").GetString().Should().Be("/app/name");
        changes[0].GetProperty("kind").GetString().Should().Be("changed");
        changes[0].GetProperty("from").GetString().Should().Be("\"Acme\"");
        changes[0].GetProperty("to").GetString().Should().Be("\"Acme Two\"");
    }

    /// <summary>TC-S06-API-041 — a repeated key replays the first response.</summary>
    [Fact]
    public async Task A_repeated_idempotency_key_replays_the_first_response()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var config = Mutate(await CurrentAsync(tenant), "Idempotent");
        var key = Guid.NewGuid().ToString();

        var first = await PostWithKeyAsync(tenant, config, key);
        var second = await PostWithKeyAsync(tenant, config, key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        (await second.Content.ReadAsStringAsync())
            .Should().Be(await first.Content.ReadAsStringAsync());

        (await VersionCountAsync(tenant)).Should().Be(2, "one seeded version plus one save");
    }

    /// <summary>The same key with a different body is a conflict, not a replay.</summary>
    [Fact]
    public async Task Reusing_a_key_for_a_different_body_is_refused()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var current = await CurrentAsync(tenant);
        var key = Guid.NewGuid().ToString();

        await PostWithKeyAsync(tenant, Mutate(current, "First"), key);
        var second = await PostWithKeyAsync(tenant, Mutate(current, "Second"), key);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A retry after a validation failure is revalidated, not replayed.
    /// </summary>
    /// <remarks>
    /// ⚠️ Otherwise fixing the configuration and retrying with the same key —
    /// which is exactly what a client that generates one key per user action
    /// does — returns the original errors forever.
    /// </remarks>
    [Fact]
    public async Task A_retry_after_a_validation_failure_is_revalidated()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);
        var key = Guid.NewGuid().ToString();

        var broken = new JsonObject { ["schemaVersion"] = 1 };
        var rejected = await PostWithKeyAsync(tenant, broken, key);
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);

        var fixedUp = Mutate(await CurrentAsync(tenant), "Fixed");
        var accepted = await PostWithKeyAsync(tenant, fixedUp, key);

        accepted.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>A configuration carrying a NUL is refused rather than crashing the save.</summary>
    /// <remarks>
    /// ⚠️ The reason CFG_CONTROL_CHARACTER exists. PostgreSQL's jsonb cannot
    /// represent U+0000, so before that rule the save failed with a 500 naming
    /// nothing the author could act on.
    /// </remarks>
    [Fact]
    public async Task A_configuration_containing_a_nul_is_refused_cleanly()
    {
        var tenant = await TenantBuilder.CreateAsync(factory);

        var config = await CurrentAsync(tenant);
        var document = config.GetProperty("config").Deserialize<JsonObject>()!;
        document["app"]!["name"] = "Acme" + (char)0 + "Orders";

        var response = await tenant.Client.PostAsJsonAsync(Config(tenant), new { config = document });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableContent);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors")
            .EnumerateArray()
            .Select(x => x.GetProperty("code").GetString())
            .Should().Contain("CFG_CONTROL_CHARACTER");
    }

    /// <summary>Another tenant's app is invisible on every configuration route.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("/versions")]
    public async Task Another_tenants_configuration_is_not_found(string suffix)
    {
        var mine = await TenantBuilder.CreateAsync(factory);
        var theirs = await TenantBuilder.CreateAsync(factory);

        var response = await mine.Client.GetAsync(Url($"/v1/apps/{theirs.AppId}/config{suffix}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string Config(TenantClient tenant) => $"/v1/apps/{tenant.AppId}/config";

    /// <summary>Relative request URI. CA2234 asks for Uri rather than string on GetAsync.</summary>
    private static Uri Url(string path) => new(path, UriKind.Relative);

    private static async Task<JsonElement> CurrentAsync(TenantClient tenant) =>
        await tenant.Client.GetFromJsonAsync<JsonElement>(Config(tenant));

    private static JsonObject Mutate(JsonElement current, string name)
    {
        var config = current.GetProperty("config").Deserialize<JsonObject>()!;
        config["app"]!["name"] = name;
        return config;
    }

    private static async Task<HttpResponseMessage> PostWithKeyAsync(
        TenantClient tenant,
        JsonObject config,
        string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Config(tenant))
        {
            Content = JsonContent.Create(new { config }),
        };

        request.Headers.Add("Idempotency-Key", key);
        return await tenant.Client.SendAsync(request);
    }

    private static async Task<int> VersionCountAsync(TenantClient tenant)
    {
        var page = await tenant.Client.GetFromJsonAsync<JsonElement>(
            $"{Config(tenant)}/versions?limit=100");

        return page.GetProperty("items").GetArrayLength();
    }
}
