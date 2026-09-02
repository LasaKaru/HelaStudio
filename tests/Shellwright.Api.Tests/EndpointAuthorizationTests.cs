using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shellwright.Api.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// TC-S06-SEC-005 — every endpoint has made a decision about who may call it.
/// </summary>
/// <remarks>
/// ⚠️ This is the test that catches "forgot to secure the new endpoint",
/// permanently.
///
/// The fallback policy in <c>Program.cs</c> already means an undecorated
/// endpoint requires authentication, so the failure this guards against is not
/// an open door. It is subtler: an endpoint whose author never thought about
/// authorisation, which today happens to inherit a sensible default and
/// tomorrow is moved, grouped, or copied somewhere that default does not
/// apply. Requiring the decision to be written down makes it visible in the
/// diff where it is made.
/// </remarks>
[Collection(DatabaseFixtureDefinition.Name)]
public sealed class EndpointAuthorizationTests(PostgresFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture);

    /// <inheritdoc />
    public void Dispose() => factory.Dispose();

    [Fact]
    public void Every_endpoint_declares_an_authorisation_decision()
    {
        var undecided = new List<string>();

        foreach (var endpoint in Endpoints())
        {
            var requiresAuthorisation = endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null;
            var explicitlyAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

            if (!requiresAuthorisation && !explicitlyAnonymous)
            {
                undecided.Add(Describe(endpoint));
            }
        }

        undecided.Should().BeEmpty(
            "each of these needs either .RequireAuthorization() or an explicit .AllowAnonymous()");
    }

    /// <summary>The set of anonymous endpoints is small, and each one is listed on purpose.</summary>
    /// <remarks>
    /// The previous test only asks that a decision was made. This one asks
    /// whether it was the right decision, by making "this endpoint is open to
    /// the internet" something a reviewer has to approve here as well as at the
    /// call site.
    /// </remarks>
    [Fact]
    public void The_anonymous_endpoints_are_the_expected_ones()
    {
        var expected = new[]
        {
            "GET /health/live",
            "POST /v1/auth/register",
            "POST /v1/auth/login",
            "POST /v1/auth/refresh",
            "POST /v1/auth/logout",
            "POST /v1/auth/verify-email",
            "POST /v1/auth/password/forgot",
            "POST /v1/auth/password/reset",
            "GET /v1/auth/oauth/{provider}",
            "GET /v1/auth/oauth/{provider}/complete",
        };

        var anonymous = Endpoints()
            .Where(x => x.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .ToList();

        anonymous.Should().BeEquivalentTo(expected);
    }

    private IEnumerable<RouteEndpoint> Endpoints()
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        return source.Endpoints.OfType<RouteEndpoint>();
    }

    private static string Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
        var pattern = "/" + endpoint.RoutePattern.RawText?.TrimStart('/').TrimEnd('/');

        return $"{string.Join(",", methods)} {pattern}";
    }
}
