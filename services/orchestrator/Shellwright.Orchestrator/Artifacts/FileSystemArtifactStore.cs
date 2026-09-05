using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Artifacts;

/// <summary>Artifact storage settings.</summary>
public sealed class ArtifactStorageOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "ArtifactStorage";

    /// <summary>Directory artifacts are written to.</summary>
    [Required]
    public string Directory { get; set; } = string.Empty;

    /// <summary>
    /// The largest artifact that will be accepted.
    /// </summary>
    /// <remarks>
    /// ⚠️ A bound, because the alternative is unbounded. A misconfigured build
    /// that packages a customer's whole media library produces a multi-gigabyte
    /// APK, and the first thing that notices is the host running out of disk
    /// during somebody else's build.
    /// </remarks>
    [Range(1_000_000, 8_000_000_000)]
    public long MaxArtifactBytes { get; set; } = 2_000_000_000;
}

/// <summary>
/// Stores build artifacts in a directory, addressed by their own digest.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is what runs today and is not what should run in production — the
/// plan is Cloudflare R2, which needs credentials this project does not have.
/// The seam is an interface so swapping it is one class, and the gap is in
/// <c>ACTION_REQUIRED.md</c> rather than hidden behind a name implying
/// otherwise.
/// </para>
/// <para>
/// ⚠️ Everything here streams. An APK is tens of megabytes, an AAB with several
/// density splits more, and several builds finish at once; reading one into a
/// <c>byte[]</c> puts it on the large object heap and makes artifact size a
/// cause of orchestrator crashes.
/// </para>
/// </remarks>
/// <param name="options">Storage settings.</param>
public sealed class FileSystemArtifactStore(IOptions<ArtifactStorageOptions> options) : IArtifactStore
{
    /// <summary>The scheme every artifact reference uses.</summary>
    public const string ReferenceScheme = "artifact://sha256-";

    private readonly ArtifactStorageOptions settings =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<UploadedArtifact> StoreAsync(
        BuildRequest request,
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        var source = new FileInfo(artifactPath);

        if (!source.Exists)
        {
            throw new FileNotFoundException(
                "The build reported success but left no artifact at the expected path.",
                artifactPath);
        }

        if (source.Length > settings.MaxArtifactBytes)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The artifact is {source.Length:N0} bytes, over the {settings.MaxArtifactBytes:N0} byte limit."));
        }

        var digest = await DigestAsync(artifactPath, cancellationToken);
        var destination = PathFor(digest);

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            // Content-addressed, so an identical artifact is already the same
            // bytes. Two builds of the same configuration cost one copy.
            return new UploadedArtifact(Reference(digest), source.Length);
        }

        // Staged and moved, so a crash mid-copy leaves nothing rather than a
        // truncated file that would then be trusted because its name says what
        // it should contain.
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var reading = source.OpenRead())
            await using (var writing = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await reading.CopyToAsync(writing, cancellationToken);
            }

            File.Move(staging, destination, overwrite: false);
        }
        catch (IOException) when (File.Exists(destination))
        {
            // Another build stored the same bytes first. Its copy is this copy.
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }

        return new UploadedArtifact(Reference(digest), source.Length);
    }

    /// <inheritdoc />
    public async Task<long> FetchAsync(
        string artifactReference,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var source = PathFor(DigestOf(artifactReference));

        if (!File.Exists(source))
        {
            throw new FileNotFoundException(
                $"No stored artifact for {artifactReference}.",
                source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);

        await using var reading = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var writing = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await reading.CopyToAsync(writing, cancellationToken);

        return writing.Length;
    }

    /// <summary>Builds the reference for a digest.</summary>
    /// <param name="digest">Lowercase hex SHA-256.</param>
    /// <returns>The reference.</returns>
    public static string Reference(string digest) => ReferenceScheme + digest;

    /// <summary>Reads the digest back out of a reference.</summary>
    /// <param name="artifactReference">The reference.</param>
    /// <returns>The digest.</returns>
    /// <remarks>
    /// ⚠️ Validated, not merely parsed. A reference reaches here from a database
    /// row, and a digest is about to be turned into a filesystem path; a value
    /// containing <c>../</c> would read whatever the orchestrator can read.
    /// </remarks>
    public static string DigestOf(string artifactReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactReference);

        if (!artifactReference.StartsWith(ReferenceScheme, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"'{artifactReference}' is not an artifact reference.",
                nameof(artifactReference));
        }

        var digest = artifactReference[ReferenceScheme.Length..];

        if (digest.Length != 64 || !IsLowerHex(digest))
        {
            throw new ArgumentException(
                "An artifact reference must carry a 64-character lowercase hex SHA-256.",
                nameof(artifactReference));
        }

        return digest;
    }

    private static bool IsLowerHex(string value)
    {
        foreach (var character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<string> DigestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return Convert.ToHexStringLower(hash);
    }

    private string PathFor(string digest) =>

        // Two levels of fan-out. A single directory with a hundred thousand
        // artifacts in it is slow to list on every filesystem and hostile on
        // some.
        Path.Combine(settings.Directory, digest[..2], digest[2..4], digest);
}
