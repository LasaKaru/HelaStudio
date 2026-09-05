using System.Net;
using System.Net.Sockets;

namespace Shellwright.Api.Config;

/// <summary>
/// Refuses URLs that point back into the infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ TC-S06-SEC-006. A configuration field that later gets fetched — for a
/// screenshot, a favicon, a manifest, a reachability probe — is a
/// server-side request forgery primitive if the URL is not checked. The
/// canonical target is <c>169.254.169.254</c>, the cloud metadata endpoint,
/// which on most providers hands out credentials to anything that asks from
/// inside the network.
/// </para>
/// <para>
/// The check runs at storage time rather than at fetch time on purpose. There
/// will be several fetchers — site analysis, icon extraction, link
/// verification — and guarding each of them is a rule somebody eventually
/// forgets. Guarding the field means the dangerous value never enters the
/// system.
/// </para>
/// <para>
/// ⚠️ It is necessary but not sufficient. A hostname that resolves to a public
/// address now can resolve to a private one later, so anything that actually
/// makes the request must re-resolve and re-check immediately before
/// connecting, and must not follow redirects blindly. That belongs with the
/// fetcher, and this class exists so that the fetcher's guard is the second
/// one rather than the only one.
/// </para>
/// </remarks>
/// <param name="resolver">DNS lookup, replaceable in tests.</param>
public sealed class UrlSafety(IDnsResolver resolver)
{
    /// <summary>Checks a URL, returning a reason when it must be refused.</summary>
    /// <param name="url">The URL as the caller wrote it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Null when the URL is acceptable, otherwise a message for the caller.</returns>
    public async Task<string?> CheckAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return "This is not an absolute URL.";
        }

        if (parsed.Scheme is not ("http" or "https"))
        {
            return $"'{parsed.Scheme}' is not a scheme an app can load. Use https.";
        }

        // Userinfo is a redirect-confusion trick as much as anything: a browser
        // shows one host in a URL like https://real.example.com@attacker.test/
        // and connects to another.
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            return "A URL with embedded credentials is not allowed.";
        }

        IReadOnlyList<IPAddress> addresses;

        if (IPAddress.TryParse(parsed.Host.Trim('[', ']'), out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await resolver.ResolveAsync(parsed.DnsSafeHost, cancellationToken);
            }
            catch (SocketException)
            {
                // ⚠️ A name that does not resolve is refused rather than
                // accepted. Accepting it would mean the check is skipped for
                // exactly the hostnames an attacker controls the DNS for.
                return $"'{parsed.DnsSafeHost}' does not resolve.";
            }
        }

        if (addresses.Count == 0)
        {
            return $"'{parsed.DnsSafeHost}' does not resolve.";
        }

        foreach (var address in addresses)
        {
            if (Describe(address) is { } reason)
            {
                return $"'{parsed.Host}' resolves to {address}, which is {reason}. "
                    + "An app's start page has to be reachable from a phone.";
            }
        }

        return null;
    }

    /// <summary>Names why an address is not routable from the public internet.</summary>
    /// <param name="address">The address to classify.</param>
    /// <returns>A description, or null when the address is ordinary.</returns>
    public static string? Describe(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // An IPv4 address wrapped in IPv6 reaches the same host by a different
        // spelling, and is the standard way this check gets bypassed.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return "a loopback address";
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();

            return octets switch
            {
                // The metadata endpoint. Named separately from the rest of
                // 169.254/16 because it is the one that hands out credentials.
                [169, 254, 169, 254] => "the cloud metadata endpoint",
                [169, 254, ..] => "a link-local address",
                [10, ..] => "a private address",
                [172, >= 16 and <= 31, ..] => "a private address",
                [192, 168, ..] => "a private address",
                [127, ..] => "a loopback address",
                [0, ..] => "an unspecified address",
                [100, >= 64 and <= 127, ..] => "a carrier-grade NAT address",
                [>= 224, ..] => "a multicast or reserved address",
                _ => null,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal)
            {
                return "a link-local address";
            }

            if (address.IsIPv6SiteLocal || address.IsIPv6Multicast)
            {
                return "a site-local or multicast address";
            }

            // fc00::/7, the IPv6 equivalent of 10/8.
            if ((address.GetAddressBytes()[0] & 0xFE) == 0xFC)
            {
                return "a unique local address";
            }

            if (address.Equals(IPAddress.IPv6Any))
            {
                return "an unspecified address";
            }
        }

        return null;
    }
}

/// <summary>Resolves host names to addresses.</summary>
public interface IDnsResolver
{
    /// <summary>Looks up every address for a host.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every address the name resolves to.</returns>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default);
}

/// <summary>Resolves through the operating system.</summary>
public sealed class SystemDnsResolver : IDnsResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken = default) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}
