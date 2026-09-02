using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Shellwright.Api.Auth;

/// <summary>Mints the short-lived access tokens the studio sends on every request.</summary>
/// <param name="options">Authentication settings.</param>
/// <param name="clock">Time source, so tests can reason about expiry.</param>
public sealed class AccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly AuthOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Builds the key both this issuer and the bearer handler use.</summary>
    /// <param name="signingKey">Base64 signing key from configuration.</param>
    /// <returns>A symmetric key.</returns>
    /// <exception cref="InvalidOperationException">The key is missing or too short.</exception>
    public static SymmetricSecurityKey CreateKey(string signingKey)
    {
        var bytes = DecodeKey(signingKey);

        // HS256 with a key shorter than its output is a downgrade nobody
        // notices: the token still verifies, and the security margin is gone.
        return bytes.Length >= 32
            ? new SymmetricSecurityKey(bytes)
            : throw new InvalidOperationException(
                "Auth:SigningKey must decode to at least 32 bytes. Generate one with `openssl rand -base64 32`.");
    }

    /// <summary>Issues an access token for a user.</summary>
    /// <param name="userId">The subject.</param>
    /// <param name="email">The subject's address, carried so the studio need not fetch it.</param>
    /// <returns>The compact token and the moment it expires.</returns>
    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, string email)
    {
        var now = clock.GetUtcNow();
        var expires = now + settings.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, email),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                ],
                AuthSchemes.AccessToken),
            SigningCredentials = new SigningCredentials(
                CreateKey(settings.SigningKey),
                SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    private static byte[] DecodeKey(string signingKey)
    {
        try
        {
            return Convert.FromBase64String(signingKey);
        }
        catch (FormatException)
        {
            // A key pasted as plain text still has to work, or the first
            // deployment fails on something that looks like a typo.
            return Encoding.UTF8.GetBytes(signingKey);
        }
    }
}
