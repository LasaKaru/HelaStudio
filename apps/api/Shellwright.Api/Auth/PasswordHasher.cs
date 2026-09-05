using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Shellwright.Api.Auth;

/// <summary>The outcome of checking a password against a stored hash.</summary>
public enum PasswordVerification
{
    /// <summary>The password does not match.</summary>
    Failed = 0,

    /// <summary>The password matches.</summary>
    Success = 1,

    /// <summary>The password matches, but the hash uses outdated parameters and should be replaced.</summary>
    SuccessRehashNeeded = 2,
}

/// <summary>
/// Argon2id password hashing.
/// </summary>
/// <remarks>
/// <para>
/// Parameters follow RFC 9106's second recommended option — 64 MiB, three
/// passes, four lanes — which is the memory-constrained profile. It is chosen
/// deliberately: the control plane runs on a 12 GB host shared with Postgres,
/// Redis, and build containers, and a login storm at 2 GiB of working memory
/// per attempt is a denial of service we would have built ourselves.
/// </para>
/// <para>
/// ⚠️ The parameters are stored in each hash string rather than read from
/// configuration at verification time. Raising them later must not invalidate
/// every existing password; encoding them per hash is what makes an upgrade a
/// gradual rehash-on-login instead of a mass reset.
/// </para>
/// </remarks>
public sealed class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int CurrentMemoryKib = 65536;
    private const int CurrentIterations = 3;
    private const int CurrentLanes = 4;

    /// <summary>
    /// A hash of a value nobody knows, used to spend the same time on a
    /// non-existent account as on a real one.
    /// </summary>
    private readonly Lazy<string> decoy = new(() =>
        new PasswordHasher().Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    /// <summary>Hashes a password with the current parameters.</summary>
    /// <param name="password">The plaintext password.</param>
    /// <returns>An encoded hash string, safe to store.</returns>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, CurrentMemoryKib, CurrentIterations, CurrentLanes);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"$argon2id$v=19$m={CurrentMemoryKib},t={CurrentIterations},p={CurrentLanes}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <summary>Checks a password against a stored hash.</summary>
    /// <param name="password">The plaintext password offered.</param>
    /// <param name="encoded">The stored hash.</param>
    /// <returns>Whether it matched, and whether the stored hash is out of date.</returns>
    public PasswordVerification Verify(string password, string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        if (!TryParse(encoded, out var parameters, out var salt, out var expected))
        {
            return PasswordVerification.Failed;
        }

        var actual = Derive(password, salt, parameters.MemoryKib, parameters.Iterations, parameters.Lanes);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        var current = parameters.MemoryKib == CurrentMemoryKib
            && parameters.Iterations == CurrentIterations
            && parameters.Lanes == CurrentLanes;

        return current ? PasswordVerification.Success : PasswordVerification.SuccessRehashNeeded;
    }

    /// <summary>
    /// Spends roughly the same time as a real verification, and always fails.
    /// </summary>
    /// <param name="password">The password offered for an account that does not exist.</param>
    /// <remarks>
    /// ⚠️ Without this, an unknown address returns in a millisecond and a known
    /// one in a hundred, which turns the login endpoint into an account
    /// enumeration oracle — the thing rate limiting cannot fix, because a
    /// single request already answers the question.
    /// </remarks>
    public void VerifyDecoy(string password) => Verify(password, decoy.Value);

    private static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int lanes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = lanes,
        };

        return argon.GetBytes(HashBytes);
    }

    private static bool TryParse(
        string encoded,
        out (int MemoryKib, int Iterations, int Lanes) parameters,
        out byte[] salt,
        out byte[] hash)
    {
        parameters = default;
        salt = [];
        hash = [];

        // $argon2id$v=19$m=65536,t=3,p=4$<salt>$<hash>
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
        {
            return false;
        }

        int memory = 0, iterations = 0, lanes = 0;
        foreach (var setting in parts[3].Split(','))
        {
            var pair = setting.Split('=');
            if (pair.Length != 2 || !int.TryParse(pair[1], CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            switch (pair[0])
            {
                case "m": memory = value; break;
                case "t": iterations = value; break;
                case "p": lanes = value; break;
                default: return false;
            }
        }

        if (memory <= 0 || iterations <= 0 || lanes <= 0)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[4]);
            hash = Convert.FromBase64String(parts[5]);
        }
        catch (FormatException)
        {
            return false;
        }

        parameters = (memory, iterations, lanes);
        return salt.Length > 0 && hash.Length > 0;
    }
}
