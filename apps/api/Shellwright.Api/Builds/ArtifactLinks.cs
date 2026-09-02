using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Shellwright.Api.Auth;

namespace Shellwright.Api.Builds;

/// <summary>
/// Short-lived download links for build artifacts.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Signed and expiring, because the alternatives are worse in ways that are
/// not obvious. Serving the artifact from an authenticated endpoint means the
/// bearer token travels wherever the link does — into a browser's history, a
/// chat message, a CI log. Serving it from an unauthenticated one addressed by
/// artifact hash means anyone who learns the hash has the binary forever.
/// </para>
/// <para>
/// ⚠️ The signature covers the build, the artifact and the expiry together. A
/// signature over the artifact alone would let a link issued for one build be
/// replayed against another that happens to have produced identical bytes —
/// which, given a content-addressed store, is exactly what a cache hit
/// produces.
/// </para>
/// <para>
/// ⚠️ HMAC over the same key the access tokens use, and compared in constant
/// time. A string equality check here leaks the signature a byte at a time to
/// anyone willing to make a few million requests.
/// </para>
/// </remarks>
/// <param name="options">Where the signing key lives.</param>
/// <param name="clock">Time source.</param>
public sealed class ArtifactLinks(IOptions<AuthOptions> options, TimeProvider clock)
{
    /// <summary>
    /// How long a link stays valid.
    /// </summary>
    /// <remarks>
    /// Long enough to click and for a large download to start, short enough
    /// that a link pasted into a chat is dead before anyone reads the channel
    /// history.
    /// </remarks>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly AuthOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Issues a link for one artifact of one build.</summary>
    /// <param name="buildId">The build.</param>
    /// <param name="artifactReference">What it produced.</param>
    /// <returns>The query string a caller appends, without a leading question mark.</returns>
    public string Issue(Guid buildId, string artifactReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactReference);

        var expiresAt = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var signature = Sign(buildId, artifactReference, expiresAt);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"expires={expiresAt}&signature={signature}");
    }

    /// <summary>Checks a link that came back.</summary>
    /// <param name="buildId">The build being asked for.</param>
    /// <param name="artifactReference">The artifact that build produced.</param>
    /// <param name="expires">The <c>expires</c> parameter, as sent.</param>
    /// <param name="signature">The <c>signature</c> parameter, as sent.</param>
    /// <returns>Whether the link is genuine and still valid.</returns>
    public bool IsValid(Guid buildId, string artifactReference, string? expires, string? signature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactReference);

        if (string.IsNullOrWhiteSpace(expires) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!long.TryParse(expires, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiresAt))
        {
            return false;
        }

        // ⚠️ Expiry checked before the signature is computed, so an obviously
        // dead link costs nothing. It is checked again nowhere else: the
        // signature covers the expiry, so a client cannot move it.
        if (clock.GetUtcNow().ToUnixTimeSeconds() > expiresAt)
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(Sign(buildId, artifactReference, expiresAt));
        var received = Encoding.UTF8.GetBytes(signature);

        // Length is compared first because FixedTimeEquals requires equal
        // lengths; the length of a signature is not a secret.
        return expected.Length == received.Length
            && CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private string Sign(Guid buildId, string artifactReference, long expiresAt)
    {
        // ⚠️ Length-prefixed rather than concatenated with a separator. Joining
        // fields with a character that can appear inside one of them means two
        // different tuples can produce the same string to sign, and an attacker
        // who controls part of a field chooses which.
        var payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{buildId:N}|{artifactReference.Length}:{artifactReference}|{expiresAt}");

        var key = Convert.FromBase64String(settings.SigningKey);
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));

        return Base64Url(signature);
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
