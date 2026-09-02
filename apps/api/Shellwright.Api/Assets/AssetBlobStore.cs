using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Shellwright.Api.Assets;

/// <summary>Stores asset bytes, addressed by their own digest.</summary>
/// <remarks>
/// ⚠️ Content-addressed, so a write is idempotent by construction: the same
/// bytes always land in the same place, and two organisations uploading the
/// same icon cost one copy. It also means a corrupted or substituted object is
/// detectable, which matters because the next thing that reads it embeds it in
/// a signed binary.
/// </remarks>
public interface IAssetBlobStore
{
    /// <summary>Writes bytes under their digest, doing nothing if already present.</summary>
    /// <param name="digest">Lowercase hex SHA-256 of the content.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the object is durable.</returns>
    Task WriteAsync(string digest, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>Reads bytes back, verifying them against their address.</summary>
    /// <param name="digest">Lowercase hex SHA-256.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The bytes, or null when the object is absent.</returns>
    Task<byte[]?> ReadAsync(string digest, CancellationToken cancellationToken = default);
}

/// <summary>Blob storage settings.</summary>
public sealed class AssetStorageOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "AssetStorage";

    /// <summary>Directory objects are written to.</summary>
    [Required]
    public string Directory { get; set; } = string.Empty;
}

/// <summary>
/// A blob store backed by a directory.
/// </summary>
/// <remarks>
/// ⚠️ This is what runs today, and it is not what should run in production —
/// the plan is Cloudflare R2, which needs credentials this project does not yet
/// have. The seam is here so that swapping it is one class rather than a
/// refactor, and the gap is recorded in ACTION_REQUIRED.md rather than hidden
/// behind an interface name that implies otherwise.
/// </remarks>
/// <param name="options">Storage settings.</param>
public sealed class FileSystemAssetBlobStore(IOptions<AssetStorageOptions> options) : IAssetBlobStore
{
    private readonly string root = options?.Value.Directory ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task WriteAsync(
        string digest,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(digest);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path))
        {
            return;
        }

        // Written to a temporary name and moved into place, so a crash halfway
        // through leaves no file rather than a truncated one that would then
        // be trusted because its name says what it should contain.
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(staging, content, cancellationToken);
            File.Move(staging, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another request wrote the identical bytes first. Content
            // addressing makes that a no-op rather than a conflict.
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReadAsync(string digest, CancellationToken cancellationToken = default)
    {
        var path = PathFor(digest);

        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        var actual = Convert.ToHexStringLower(SHA256.HashData(content));

        // ⚠️ Verified on the way out, not only on the way in. An object that no
        // longer hashes to its own name was corrupted or substituted, and
        // returning it would put unknown bytes into a signed app.
        return string.Equals(actual, digest, StringComparison.Ordinal)
            ? content
            : throw new InvalidOperationException(
                $"Asset {digest} hashes to {actual}. The stored object has been altered.");
    }

    private string PathFor(string digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        if (digest.Length != 64 || !digest.All(char.IsAsciiHexDigitLower))
        {
            // The digest becomes a path, so anything but 64 lowercase hex
            // characters is refused before it can be one.
            throw new ArgumentException("A digest must be 64 lowercase hex characters.", nameof(digest));
        }

        // Two levels of fan-out: a directory with a million entries is slow to
        // list on every filesystem and impossible on some.
        return Path.Combine(root, digest[..2], digest[2..4], digest);
    }
}
