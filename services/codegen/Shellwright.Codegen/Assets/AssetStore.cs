using System.Globalization;
using System.Security.Cryptography;

namespace Shellwright.Codegen.Assets;

/// <summary>Raised when an asset a config depends on cannot be provided.</summary>
public sealed class AssetException : Exception
{
    /// <summary>Creates an exception naming the reference that failed.</summary>
    /// <param name="reference">The <c>asset://</c> reference.</param>
    /// <param name="message">What went wrong.</param>
    public AssetException(string reference, string message)
        : base($"{reference}: {message}") => Reference = reference;

    /// <summary>Creates an empty exception.</summary>
    public AssetException() => Reference = string.Empty;

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public AssetException(string message) : base(message) => Reference = string.Empty;

    /// <summary>Creates an exception with a message and cause.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public AssetException(string message, Exception innerException)
        : base(message, innerException) => Reference = string.Empty;

    /// <summary>The reference that failed.</summary>
    public string Reference { get; }
}

/// <summary>
/// Resolves the <c>asset://sha256-…</c> references a config carries.
/// </summary>
/// <remarks>
/// <para>
/// Assets are content-addressed, which buys three things at once: the same icon
/// uploaded by fifty customers is stored once, an asset can be cached forever
/// because its name cannot outlive its content, and a corrupted or substituted
/// file is detectable rather than merely unlikely.
/// </para>
/// <para>
/// ⚠️ The third only holds if somebody actually checks. An implementation that
/// returns bytes without verifying them turns content addressing into
/// decoration — and this store feeds icons straight into a signed binary that
/// ships to a customer's users.
/// </para>
/// </remarks>
public interface IAssetStore
{
    /// <summary>Fetches the bytes an <c>asset://</c> reference names.</summary>
    /// <param name="reference">A reference of the form <c>asset://sha256-…</c>.</param>
    /// <returns>The asset's bytes.</returns>
    /// <exception cref="AssetException">Missing, malformed, or failing verification.</exception>
    byte[] Read(string reference);
}

/// <summary>Shared parsing and verification for <c>asset://</c> references.</summary>
public static class AssetReference
{
    private const string Prefix = "asset://sha256-";

    /// <summary>Extracts the digest from a reference.</summary>
    /// <param name="reference">A reference of the form <c>asset://sha256-…</c>.</param>
    /// <returns>The 64-character lowercase hex digest.</returns>
    /// <exception cref="AssetException">The reference is not well formed.</exception>
    public static string Digest(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!reference.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new AssetException(reference, $"is not an {Prefix}… reference.");
        }

        var digest = reference[Prefix.Length..];

        return digest.Length == 64 && digest.All(char.IsAsciiHexDigitLower)
            ? digest
            : throw new AssetException(reference, "does not carry a 64-character lowercase hex digest.");
    }

    /// <summary>Checks that content matches the digest naming it.</summary>
    /// <param name="reference">The reference the content was fetched for.</param>
    /// <param name="content">The bytes fetched.</param>
    /// <exception cref="AssetException">The content does not hash to its address.</exception>
    public static void Verify(string reference, ReadOnlySpan<byte> content)
    {
        var expected = Digest(reference);
        var actual = Convert.ToHexStringLower(SHA256.HashData(content));

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            // ⚠️ Never silently accept. Whatever produced this either corrupted
            // the file or substituted it, and the next step embeds it in a
            // signed binary.
            throw new AssetException(
                reference,
                string.Create(CultureInfo.InvariantCulture, $"content hashes to {actual}, not to its own address."));
        }
    }
}

/// <summary>An <see cref="IAssetStore"/> backed by a directory of files.</summary>
/// <remarks>
/// Files are named <c>sha256-&lt;digest&gt;.&lt;ext&gt;</c>. Used by the tests and
/// by anyone generating a project by hand; production reads from R2 instead.
/// </remarks>
public sealed class DirectoryAssetStore : IAssetStore
{
    private readonly string root;

    /// <summary>Creates a store over <paramref name="root"/>.</summary>
    /// <param name="root">A directory of content-addressed files.</param>
    public DirectoryAssetStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = Path.GetFullPath(root);
    }

    /// <inheritdoc/>
    public byte[] Read(string reference)
    {
        var digest = AssetReference.Digest(reference);

        var match = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, $"sha256-{digest}.*").OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
            : null;

        if (match is null)
        {
            throw new AssetException(reference, $"no file for this digest in '{root}'.");
        }

        var content = File.ReadAllBytes(match);
        AssetReference.Verify(reference, content);
        return content;
    }
}

/// <summary>An <see cref="IAssetStore"/> holding assets in memory.</summary>
public sealed class InMemoryAssetStore : IAssetStore
{
    private readonly Dictionary<string, byte[]> assets = new(StringComparer.Ordinal);

    /// <summary>Adds content, returning the reference that now names it.</summary>
    /// <param name="content">The asset's bytes.</param>
    /// <returns>The <c>asset://</c> reference.</returns>
    public string Add(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var reference = "asset://sha256-" + Convert.ToHexStringLower(SHA256.HashData(content));
        assets[reference] = content;
        return reference;
    }

    /// <inheritdoc/>
    public byte[] Read(string reference)
    {
        var content = assets.GetValueOrDefault(reference)
            ?? throw new AssetException(reference, "was not added to this store.");

        AssetReference.Verify(reference, content);
        return content;
    }
}
