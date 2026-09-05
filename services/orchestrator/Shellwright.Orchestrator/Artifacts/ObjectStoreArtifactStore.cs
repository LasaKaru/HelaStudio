using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Artifacts;

/// <summary>Where finished artifacts live in object storage.</summary>
public sealed class ObjectStorageOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "ObjectStorage";

    /// <summary>
    /// The S3-compatible endpoint.
    /// </summary>
    /// <remarks>
    /// Cloudflare R2 is the plan: it is S3-compatible and, decisively for this
    /// product, charges nothing for egress. A build artifact is tens of
    /// megabytes and every one of them is downloaded at least once, so egress
    /// is the bill that would otherwise grow with usage rather than with
    /// storage.
    /// </remarks>
    [Required]
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>The bucket artifacts are written to.</summary>
    [Required]
    public string Bucket { get; set; } = string.Empty;

    /// <summary>Access key id.</summary>
    [Required]
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>Secret access key.</summary>
    /// <remarks>
    /// ⚠️ Never logged, and never put in a URL. It reaches here from
    /// configuration and goes straight into the credential object.
    /// </remarks>
    [Required]
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an artifact is kept.
    /// </summary>
    /// <remarks>
    /// ⚠️ A retention period exists because the filesystem store had none, and
    /// the first symptom of that is builds failing for an unrelated-looking
    /// reason once a disk fills. Ninety days is long enough that a customer can
    /// come back to a release they shipped last quarter, and short enough that
    /// storage grows with active use rather than for ever.
    ///
    /// This value is applied by a lifecycle rule on the bucket, not by this
    /// code — an object store expires objects far more cheaply than anything
    /// that has to enumerate them.
    /// </remarks>
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 90;

    /// <summary>The largest artifact that will be accepted.</summary>
    [Range(1_000_000, 8_000_000_000)]
    public long MaxArtifactBytes { get; set; } = 2_000_000_000;
}

/// <summary>
/// Stores build artifacts in an S3-compatible object store, addressed by digest.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Content-addressed, exactly like the filesystem store it replaces, so the
/// two are interchangeable and a deployment can move between them without
/// rewriting a single reference. That is the whole reason the reference format
/// is <c>artifact://sha256-…</c> rather than a URL: a URL would have baked one
/// storage backend into every row of the builds table.
/// </para>
/// <para>
/// ⚠️ Everything streams. A release IPA with several architecture slices is
/// hundreds of megabytes; buffering one to compute its digest and a second copy
/// to upload is the difference between an orchestrator that runs several builds
/// and one the OOM killer visits.
/// </para>
/// </remarks>
/// <param name="client">The S3 client.</param>
/// <param name="options">Where the bucket is.</param>
public sealed class ObjectStoreArtifactStore(
    IAmazonS3 client,
    IOptions<ObjectStorageOptions> options) : IArtifactStore
{
    private readonly ObjectStorageOptions settings =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Builds the object key for a digest.</summary>
    /// <param name="digest">Lowercase hex SHA-256.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// ⚠️ The same two-level fan-out as the filesystem store. Object stores do
    /// not need it for lookup, but a flat prefix makes every listing — for
    /// lifecycle rules, for a migration, for an audit — scan the whole bucket.
    /// </remarks>
    public static string KeyFor(string digest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"artifacts/{digest[..2]}/{digest[2..4]}/{digest}");
    }

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
        var key = KeyFor(digest);

        var usesTls = settings.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // ⚠️ Checked before uploading, because content addressing makes the
        // upload pure waste when the object is already there — and an artifact
        // is tens of megabytes over somebody's network. Two builds of the same
        // configuration are the common case, not the exotic one.
        if (await ExistsAsync(key, cancellationToken))
        {
            return new UploadedArtifact(FileSystemArtifactStore.Reference(digest), source.Length);
        }

        await using (var reading = source.OpenRead())
        {
            await client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = settings.Bucket,
                    Key = key,
                    InputStream = reading,

                    // ⚠️ Disabled only over HTTPS, and the SDK is right to
                    // insist. Signing the payload otherwise means the SDK
                    // buffers the whole stream to hash it before sending a
                    // byte, which for a large artifact is the allocation this
                    // class exists to avoid — but skipping it on plain HTTP
                    // would let anything on the path alter the body
                    // undetected. Over TLS, transport integrity is TLS's job
                    // and content integrity is the digest in the key.
                    //
                    // R2 is always HTTPS, so production takes the fast path.
                    // A local HTTP endpoint pays for payload signing instead
                    // of quietly losing the protection.
                    DisablePayloadSigning = usesTls,

                    // Not a checksum the caller supplies — the store's own
                    // record of what it believes it holds.
                    Metadata = { ["sha256"] = digest },
                },
                cancellationToken);
        }

        return new UploadedArtifact(FileSystemArtifactStore.Reference(digest), source.Length);
    }

    /// <inheritdoc />
    public async Task<long> FetchAsync(
        string artifactReference,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // Validated rather than parsed, for the same reason as the filesystem
        // store: a reference arrives from a database row and becomes a key.
        var key = KeyFor(FileSystemArtifactStore.DigestOf(artifactReference));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);

        GetObjectResponse response;

        try
        {
            response = await client.GetObjectAsync(settings.Bucket, key, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"No stored artifact for {artifactReference}.", key, exception);
        }

        using (response)
        await using (var writing = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            await response.ResponseStream.CopyToAsync(writing, cancellationToken);
            return writing.Length;
        }
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await client.GetObjectMetadataAsync(settings.Bucket, key, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
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

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}

/// <summary>Builds the S3 client the artifact store uses.</summary>
public static class ObjectStoreClientFactory
{
    /// <summary>Creates a client for an S3-compatible endpoint.</summary>
    /// <param name="settings">Where the bucket is.</param>
    /// <returns>The client.</returns>
    public static IAmazonS3 Create(ObjectStorageOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = settings.ServiceUrl,

                // ⚠️ Path style, not virtual-hosted. R2 and most S3-compatible
                // endpoints do not resolve bucket-name subdomains, and the
                // failure when this is wrong is a DNS error that says nothing
                // about buckets.
                ForcePathStyle = true,

                // R2 ignores the region but the SDK requires one to sign.
                AuthenticationRegion = "auto",
            });
    }
}
