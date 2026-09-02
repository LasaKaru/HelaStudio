using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Tests.Infrastructure;

/// <summary>An organisation, a workspace, an app, and a client signed in as their owner.</summary>
/// <param name="Client">Authenticated HTTP client.</param>
/// <param name="UserId">The signed-in account.</param>
/// <param name="OrgId">The organisation.</param>
/// <param name="WorkspaceId">A workspace in it.</param>
/// <param name="AppId">An app in that workspace.</param>
public sealed record TenantClient(
    HttpClient Client,
    Guid UserId,
    Guid OrgId,
    Guid WorkspaceId,
    Guid AppId);

/// <summary>Builds a tenant through the API, the way a customer would.</summary>
/// <remarks>
/// ⚠️ Through the endpoints rather than by inserting rows. Seeding directly
/// would skip the very code these tests exist to exercise — the claim window on
/// organisation creation, the seeded first configuration, the audit entries —
/// and would happily construct states the API cannot actually produce.
/// </remarks>
public static class TenantBuilder
{
    /// <summary>Signs up, creates an organisation, a workspace, and an app.</summary>
    /// <param name="factory">The application factory.</param>
    /// <param name="initialUrl">Start page for the app.</param>
    /// <returns>The identifiers and an authenticated client.</returns>
    public static async Task<TenantClient> CreateAsync(
        ApiFactory factory,
        string initialUrl = "https://93.184.216.34/")
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

        var userId = session.GetProperty("userId").GetGuid();

        var org = await Created(client.PostAsJsonAsync("/v1/orgs", new { name = $"Org {Guid.NewGuid():N}"[..20] }));
        var orgId = org.GetProperty("id").GetGuid();

        var workspace = await Created(
            client.PostAsJsonAsync($"/v1/orgs/{orgId}/workspaces", new { name = "Default" }));
        var workspaceId = workspace.GetProperty("id").GetGuid();

        var app = await Created(client.PostAsJsonAsync(
            $"/v1/workspaces/{workspaceId}/apps",
            new
            {
                name = "Acme",
                bundleId = $"test.acme.a{Guid.NewGuid():N}"[..40],
                initialUrl,
            }));

        return new TenantClient(client, userId, orgId, workspaceId, app.GetProperty("id").GetGuid());
    }

    /// <summary>Grants a second account a role in the same organisation.</summary>
    /// <param name="factory">The application factory.</param>
    /// <param name="owner">The tenant to join.</param>
    /// <param name="role">The role to grant.</param>
    /// <returns>A client signed in as the new member.</returns>
    public static async Task<HttpClient> AddMemberAsync(ApiFactory factory, TenantClient owner, OrgRole role)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(owner);

        var client = factory.CreateApiClient();
        var (email, password) = await ApiFactory.RegisterAsync(client);

        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

        var granted = await owner.Client.PutAsJsonAsync(
            $"/v1/orgs/{owner.OrgId}/members/{session.GetProperty("userId").GetGuid()}",
            new { role = role.ToString() });
        granted.EnsureSuccessStatusCode();

        return client;
    }

    private static async Task<JsonElement> Created(Task<HttpResponseMessage> request)
    {
        var response = await request;

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Setup call failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
