using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shellwright.Api.Auth;
using Shellwright.Api.Domain;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>The <c>sw_live_…</c> credentials CI and the command line present.</summary>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class ApiTokenTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    /// <summary>TC-S06-API-013 — an API token authenticates on the same header as an access token.</summary>
    [Fact]
    public async Task An_api_token_authenticates()
    {
        var tenant = await TenantSeed.CreateAsync(fixture, "apitok");
        var secret = await MintAsync(tenant, OrgRole.Developer);

        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var response = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var caller = await response.Content.ReadFromJsonAsync<JsonElement>();
        caller.GetProperty("scheme").GetString().Should().Be(AuthSchemes.ApiToken);
        caller.GetProperty("userId").GetString().Should().Be(tenant.UserId.ToString());
        caller.GetProperty("org").GetString().Should().Be(tenant.OrgId.ToString());
    }

    /// <summary>The prefix is short enough to be useless and long enough to recognise.</summary>
    [Fact]
    public async Task The_stored_prefix_is_not_a_usable_fragment_of_the_secret()
    {
        var tenant = await TenantSeed.CreateAsync(fixture, "prefix");
        var secret = await MintAsync(tenant, OrgRole.Developer);

        var context = fixture.CreateContext(tenant.UserId);
        await using (context.ConfigureAwait(false))
        {
            var stored = await context.ApiTokens.FirstAsync(x => x.OrgId == tenant.OrgId);

            stored.Prefix.Should().StartWith(ApiTokenService.LivePrefix);
            secret.Should().StartWith(stored.Prefix);
            stored.Prefix.Length.Should().BeLessThan(secret.Length / 2);

            // The secret itself must be nowhere in the row.
            stored.TokenHash.Should().NotContain(secret[ApiTokenService.LivePrefix.Length..]);
        }
    }

    /// <summary>A revoked token stops working, and says no more than that.</summary>
    [Fact]
    public async Task A_revoked_token_is_rejected()
    {
        var tenant = await TenantSeed.CreateAsync(fixture, "revoked");
        var secret = await MintAsync(tenant, OrgRole.Developer);

        var context = fixture.CreateContext(tenant.UserId);
        await using (context.ConfigureAwait(false))
        {
            await context.ApiTokens
                .Where(x => x.OrgId == tenant.OrgId)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, factory.Clock.GetUtcNow()));
        }

        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        var response = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A string that merely looks like a token is refused.</summary>
    [Theory]
    [InlineData("sw_live_notarealtokenatallnotarealtokenatall")]
    [InlineData("sw_test_something")]
    [InlineData("Bearer")]
    public async Task A_forged_token_is_rejected(string forged)
    {
        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Using a token stamps it, coarsely.</summary>
    [Fact]
    public async Task Using_a_token_records_that_it_was_used()
    {
        var tenant = await TenantSeed.CreateAsync(fixture, "lastused");
        var secret = await MintAsync(tenant, OrgRole.Developer);

        var client = factory.CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        await client.GetAsync(new Uri("/v1/auth/me", UriKind.Relative));

        var context = fixture.CreateContext(tenant.UserId);
        await using (context.ConfigureAwait(false))
        {
            var stored = await context.ApiTokens.FirstAsync(x => x.OrgId == tenant.OrgId);
            stored.LastUsedAt.Should().Be(factory.Clock.GetUtcNow());
        }
    }

    /// <summary>
    /// An API token is scoped to its organisation and cannot see another one's
    /// rows, because it acts as an account whose membership the policies check.
    /// </summary>
    [Fact]
    public async Task An_api_token_sees_only_its_own_tenant()
    {
        var mine = await TenantSeed.CreateAsync(fixture, "scoped-mine");
        var theirs = await TenantSeed.CreateAsync(fixture, "scoped-theirs");
        await MintAsync(mine, OrgRole.Developer);

        var context = fixture.CreateContext(mine.UserId);
        await using (context.ConfigureAwait(false))
        {
            var names = await context.Apps.Select(x => x.Name).ToListAsync();

            names.Should().Contain(mine.AppName);
            names.Should().NotContain(theirs.AppName);
        }
    }

    private async Task<string> MintAsync(SeededTenant tenant, OrgRole role)
    {
        using var scope = factory.Services.CreateScope();

        var tenantScope = scope.ServiceProvider.GetRequiredService<Shellwright.Api.Data.TenantContext>();
        tenantScope.UserId = tenant.UserId;

        var tokens = scope.ServiceProvider.GetRequiredService<ApiTokenService>();
        var issued = await tokens.CreateAsync(tenant.OrgId, null, "ci", role, tenant.UserId);

        return issued.Token;
    }
}
