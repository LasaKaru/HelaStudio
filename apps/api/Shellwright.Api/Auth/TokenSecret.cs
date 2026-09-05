using System.Security.Cryptography;
using System.Text;

namespace Shellwright.Api.Auth;

/// <summary>
/// Generates and fingerprints the opaque secrets used for refresh tokens, API
/// tokens, and emailed links.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ These are hashed with a single pass of SHA-256, not with Argon2, and that
/// is correct rather than a shortcut. Password hashing is slow because
/// passwords are low-entropy and guessable; these secrets carry 256 bits from a
/// cryptographic generator, so there is nothing to guess and no dictionary to
/// try. Using a slow KDF here would add latency to every authenticated request
/// and buy no security at all.
/// </para>
/// <para>
/// The consequence for lookup is that presenting a token is an indexed equality
/// match on the hash rather than a scan-and-compare, which removes the timing
/// side channel instead of trying to mask it.
/// </para>
/// </remarks>
public static class TokenSecret
{
    private const int SecretBytes = 32;

    /// <summary>Creates a new secret, URL-safe and without padding.</summary>
    /// <returns>A 43-character base64url string.</returns>
    public static string Create() => Base64Url.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>Fingerprints a secret for storage.</summary>
    /// <param name="secret">The secret as presented by the client.</param>
    /// <returns>Lowercase hex SHA-256.</returns>
    public static string Fingerprint(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

/// <summary>Base64url encoding, as used in the token strings this API issues.</summary>
public static class Base64Url
{
    /// <summary>Encodes bytes without padding, using the URL-safe alphabet.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The encoded string.</returns>
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
