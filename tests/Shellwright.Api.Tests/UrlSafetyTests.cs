using System.Net;
using FluentAssertions;
using Shellwright.Api.Config;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>TC-S06-SEC-006 — URLs that point back into the infrastructure.</summary>
public sealed class UrlSafetyTests
{
    /// <summary>The address ranges an app's start page can never legitimately be in.</summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/", "cloud metadata")]
    [InlineData("http://127.0.0.1:8080/", "loopback")]
    [InlineData("http://localhost:5000/", "loopback")]
    [InlineData("http://10.0.0.1/", "private")]
    [InlineData("http://172.16.5.4/", "private")]
    [InlineData("http://172.31.255.255/", "private")]
    [InlineData("http://192.168.1.1/", "private")]
    [InlineData("http://0.0.0.0/", "unspecified")]
    [InlineData("http://100.64.0.1/", "carrier-grade NAT")]
    [InlineData("http://[::1]/", "loopback")]
    [InlineData("http://[fc00::1]/", "unique local")]
    [InlineData("http://[fe80::1]/", "link-local")]
    public async Task Refuses(string url, string expected)
    {
        var safety = new UrlSafety(new StubResolver());

        var reason = await safety.CheckAsync(url);

        reason.Should().NotBeNull();
        reason.Should().Contain(expected);
    }

    /// <summary>
    /// An IPv4 address written as IPv6 reaches the same host.
    /// </summary>
    /// <remarks>
    /// ⚠️ The standard bypass. A check that only understands dotted quads sees
    /// ::ffff:169.254.169.254 as an ordinary IPv6 address and lets it through
    /// to the same metadata endpoint.
    /// </remarks>
    [Theory]
    [InlineData("http://[::ffff:169.254.169.254]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    public async Task Refuses_ipv4_wrapped_in_ipv6(string url)
    {
        var safety = new UrlSafety(new StubResolver());

        (await safety.CheckAsync(url)).Should().NotBeNull();
    }

    /// <summary>A hostname is refused when it resolves anywhere private, not only when all of it does.</summary>
    [Fact]
    public async Task Refuses_a_hostname_with_any_private_answer()
    {
        var resolver = new StubResolver
        {
            ["mixed.example.com"] = [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.1.2.3")],
        };

        var reason = await new UrlSafety(resolver).CheckAsync("https://mixed.example.com/");

        reason.Should().Contain("private");
    }

    /// <summary>A name that does not resolve is refused rather than waved through.</summary>
    [Fact]
    public async Task Refuses_a_hostname_that_does_not_resolve()
    {
        var reason = await new UrlSafety(new StubResolver()).CheckAsync("https://nothing.invalid/");

        reason.Should().Contain("does not resolve");
    }

    /// <summary>Schemes an app cannot load.</summary>
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    [InlineData("ftp://example.com/")]
    public async Task Refuses_other_schemes(string url) =>
        (await new UrlSafety(new StubResolver()).CheckAsync(url)).Should().NotBeNull();

    /// <summary>Embedded credentials are a host-confusion trick as much as a leak.</summary>
    [Fact]
    public async Task Refuses_embedded_credentials()
    {
        var resolver = new StubResolver { ["attacker.test"] = [IPAddress.Parse("93.184.216.34")] };

        var reason = await new UrlSafety(resolver).CheckAsync("https://real.example.com@attacker.test/");

        reason.Should().Contain("credentials");
    }

    /// <summary>An ordinary public URL passes.</summary>
    [Fact]
    public async Task Accepts_an_ordinary_public_url()
    {
        var resolver = new StubResolver { ["app.acme.com"] = [IPAddress.Parse("93.184.216.34")] };

        (await new UrlSafety(resolver).CheckAsync("https://app.acme.com/orders")).Should().BeNull();
    }

    /// <summary>A public IP literal passes without a lookup.</summary>
    [Fact]
    public async Task Accepts_a_public_literal() =>
        (await new UrlSafety(new StubResolver()).CheckAsync("https://93.184.216.34/")).Should().BeNull();

    /// <summary>A resolver whose answers the test controls, so no test needs the network.</summary>
    private sealed class StubResolver : IDnsResolver
    {
        private readonly Dictionary<string, IPAddress[]> answers = new(StringComparer.OrdinalIgnoreCase)
        {
            // Present because the checks below use it, and because resolving it
            // for real would give a different answer on a machine with an
            // unusual hosts file.
            ["localhost"] = [IPAddress.Loopback],
        };

        public IPAddress[] this[string host]
        {
            set => answers[host] = value;
        }

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                answers.TryGetValue(host, out var found) ? found : []);
    }
}
