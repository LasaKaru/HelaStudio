using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>Resource-based authorisation over real HTTP.</summary>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class AuthorizationTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    /// <summary>Creating an organisation makes you its owner.</summary>
    [Fact]
    public async Task Creating_an_organisation_makes_you_its_owner()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsJsonAsync("/v1/orgs", new { name = "Acme Software" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var org = await response.Content.ReadFromJsonAsync<JsonElement>();
        org.GetProperty("role").GetString().Should().Be("Owner");
        org.GetProperty("slug").GetString().Should().Be("acme-software");
    }

    /// <summary>A new account sees no organisations at all, not everybody's.</summary>
    [Fact]
    public async Task A_new_account_sees_no_organisations()
    {
        var other = await SignedInClientAsync();
        await other.PostAsJsonAsync("/v1/orgs", new { name = "Somebody Else Ltd" });

        var client = await SignedInClientAsync();
        var response = await client.GetAsync(new Uri("/v1/orgs", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// TC-S06-SEC-002 — another tenant's organisation is 404, not 403.
    /// </summary>
    /// <remarks>
    /// ⚠️ 403 would confirm the identifier is real, which is all an attacker
    /// needs to map a competitor's estate.
    /// </remarks>
    [Fact]
    public async Task Another_tenants_organisation_is_not_found_rather_than_forbidden()
    {
        var owner = await SignedInClientAsync();
        var created = await owner.PostAsJsonAsync("/v1/orgs", new { name = "Private Co" });
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var stranger = await SignedInClientAsync();
        var response = await stranger.GetAsync(new Uri($"/v1/orgs/{orgId}/workspaces", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>An organisation that never existed answers the same way.</summary>
    [Fact]
    public async Task An_imaginary_organisation_answers_the_same_as_a_hidden_one()
    {
        var client = await SignedInClientAsync();

        var response = await client.GetAsync(
            new Uri($"/v1/orgs/{Guid.CreateVersion7()}/workspaces", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A viewer may read and may not write.</summary>
    [Fact]
    public async Task A_viewer_can_read_but_not_create_a_workspace()
    {
        var (orgId, viewer) = await OrgWithMemberAsync(OrgRole.Viewer);

        var read = await viewer.GetAsync(new Uri($"/v1/orgs/{orgId}/workspaces", UriKind.Relative));
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var write = await viewer.PostAsJsonAsync($"/v1/orgs/{orgId}/workspaces", new { name = "Nope" });
        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>A developer may not create workspaces; an admin may.</summary>
    [Fact]
    public async Task Creating_a_workspace_needs_admin()
    {
        var (orgId, developer) = await OrgWithMemberAsync(OrgRole.Developer);
        var forbidden = await developer.PostAsJsonAsync($"/v1/orgs/{orgId}/workspaces", new { name = "Mobile" });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (adminOrg, admin) = await OrgWithMemberAsync(OrgRole.Admin);
        var allowed = await admin.PostAsJsonAsync($"/v1/orgs/{adminOrg}/workspaces", new { name = "Mobile" });
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>An admin cannot promote anybody to owner.</summary>
    [Fact]
    public async Task An_admin_cannot_grant_a_role_above_their_own()
    {
        var (orgId, admin) = await OrgWithMemberAsync(OrgRole.Admin);
        var outsider = await TenantSeed.CreateUserAsync(fixture);

        var response = await admin.PutAsJsonAsync(
            $"/v1/orgs/{orgId}/members/{outsider}",
            new { role = nameof(OrgRole.Owner) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>The last owner cannot demote themselves into an unadministrable organisation.</summary>
    [Fact]
    public async Task The_last_owner_cannot_be_demoted()
    {
        var client = await SignedInClientAsync();
        var created = await client.PostAsJsonAsync("/v1/orgs", new { name = "Solo Ltd" });
        var org = await created.Content.ReadFromJsonAsync<JsonElement>();
        var orgId = org.GetProperty("id").GetGuid();

        var members = await client.GetAsync(new Uri($"/v1/orgs/{orgId}/members", UriKind.Relative));
        var owner = (await members.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("userId").GetGuid();

        var response = await client.PutAsJsonAsync(
            $"/v1/orgs/{orgId}/members/{owner}",
            new { role = nameof(OrgRole.Admin) });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>An API token cannot be given a role above its creator's.</summary>
    [Fact]
    public async Task A_token_cannot_exceed_its_creators_role()
    {
        var (orgId, developer) = await OrgWithMemberAsync(OrgRole.Developer);

        var tooHigh = await developer.PostAsJsonAsync(
            $"/v1/orgs/{orgId}/tokens",
            new { name = "ci", role = nameof(OrgRole.Admin) });
        tooHigh.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var allowed = await developer.PostAsJsonAsync(
            $"/v1/orgs/{orgId}/tokens",
            new { name = "ci", role = nameof(OrgRole.Developer) });
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// A token stops being an admin token the moment its creator is demoted.
    /// </summary>
    /// <remarks>
    /// ⚠️ The property that makes long-lived credentials tolerable. If the role
    /// were copied into the token at mint time, revoking somebody's access
    /// would mean hunting down every token they ever created.
    /// </remarks>
    [Fact]
    public async Task Demoting_a_creator_narrows_their_tokens()
    {
        var client = await SignedInClientAsync();
        var created = await client.PostAsJsonAsync("/v1/orgs", new { name = "Demotion Ltd" });
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var minted = await client.PostAsJsonAsync(
            $"/v1/orgs/{orgId}/tokens",
            new { name = "ci", role = nameof(OrgRole.Owner) });
        minted.StatusCode.Should().Be(HttpStatusCode.Created);

        var secret = (await minted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var tokenClient = factory.CreateApiClient();
        tokenClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var beforeDemotion = await tokenClient.PostAsJsonAsync(
            $"/v1/orgs/{orgId}/workspaces",
            new { name = "Before" });
        beforeDemotion.StatusCode.Should().Be(HttpStatusCode.Created);

        // Demote the creator directly, which is what a second owner removing
        // their colleague's access amounts to.
        //
        // ⚠️ As the schema owner, not through the application role. A statement
        // run with no tenant identity stamped matches no rows and reports
        // success, so the test would demote nobody and then pass or fail for
        // reasons unrelated to what it is checking. The affected-row assertion
        // is there to make that failure loud if it ever comes back.
        var connection = await fixture.OpenAsOwnerAsync();
        await using (connection.ConfigureAwait(false))
        {
            var demote = new Npgsql.NpgsqlCommand(
                "UPDATE org_members SET role = 'Viewer' WHERE org_id = @org",
                connection);

            await using (demote.ConfigureAwait(false))
            {
                demote.Parameters.AddWithValue("org", orgId);
                (await demote.ExecuteNonQueryAsync()).Should().Be(1);
            }
        }

        var afterDemotion = await tokenClient.PostAsJsonAsync(
            $"/v1/orgs/{orgId}/workspaces",
            new { name = "After" });
        afterDemotion.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>An anonymous request reaches nothing, because the fallback policy denies.</summary>
    [Theory]
    [InlineData("/v1/orgs")]
    [InlineData("/v1/auth/me")]
    public async Task An_unauthenticated_request_is_refused(string path)
    {
        var client = factory.CreateApiClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    /// <summary>Builds an organisation and returns a client signed in as a member with the given role.</summary>
    private async Task<(Guid OrgId, HttpClient Client)> OrgWithMemberAsync(OrgRole role)
    {
        var ownerClient = await SignedInClientAsync();
        var created = await ownerClient.PostAsJsonAsync("/v1/orgs", new { name = $"Org {Guid.NewGuid():N}"[..20] });
        created.EnsureSuccessStatusCode();
        var orgId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var memberClient = await SignedInClientAsync();
        var me = await memberClient.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));
        var memberId = (await me.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();

        var granted = await ownerClient.PutAsJsonAsync(
            $"/v1/orgs/{orgId}/members/{memberId}",
            new { role = role.ToString() });
        granted.EnsureSuccessStatusCode();

        return (orgId, memberClient);
    }
}
