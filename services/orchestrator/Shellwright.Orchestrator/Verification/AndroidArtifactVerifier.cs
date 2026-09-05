using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Verification;

/// <summary>Artifact verification settings.</summary>
public sealed class VerificationOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Verification";

    /// <summary>
    /// The largest artifact a person is allowed to be handed.
    /// </summary>
    /// <remarks>
    /// Google Play's own limit for an APK is 100 MB, and an app over it cannot
    /// be published at all. Catching that here means the customer is told while
    /// they can still do something about it, rather than by the Play Console
    /// after they have waited for a build.
    /// </remarks>
    [Range(1_000_000, 4_000_000_000)]
    public long MaxArtifactBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>The smallest artifact that could possibly be real.</summary>
    /// <remarks>
    /// ⚠️ A floor, because the failure this catches is a build that "succeeded"
    /// and produced a stub. Gradle exits zero in more situations than people
    /// expect, and a 12 kB APK reaching a customer is worse than a failed build.
    /// </remarks>
    [Range(1_000, 100_000_000)]
    public long MinArtifactBytes { get; set; } = 200 * 1024;
}

/// <summary>
/// Checks an Android artifact before anybody is allowed to download it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This is a structural check, not a signature verification. It reads the
/// archive and asserts the things whose absence means the build lied: the file
/// is a real zip, it holds a compiled manifest, it holds the configuration the
/// shell reads at run time, it carries a signature, and its size is plausible.
/// Cryptographic verification is <c>apksigner verify</c>, which runs on the
/// runner where the signing tools are — this is the check that runs everywhere
/// and catches the cheap, common failures.
/// </para>
/// <para>
/// ⚠️ Every check exists because of a specific way a green build produces
/// something unusable. Gradle can exit zero having assembled nothing; a patched
/// APK can lose its signature; a misconfigured packaging step can drop the
/// assets directory, producing an app that installs, launches, and shows a
/// blank screen.
/// </para>
/// </remarks>
/// <param name="options">Verification settings.</param>
public sealed class AndroidArtifactVerifier(IOptions<VerificationOptions> options) : IArtifactVerifier
{
    private const string ManifestEntry = "AndroidManifest.xml";
    private const string DexEntryPrefix = "classes";

    private readonly VerificationOptions settings =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public Task<VerificationVerdict> VerifyAsync(
        BuildRequest request,
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        if (request.Platform != BuildPlatform.Android)
        {
            // ⚠️ Rejected rather than passed. A verifier that returns "fine" for
            // a platform it cannot inspect is worse than no verifier, because
            // the whole point is that nothing ships unchecked.
            return Task.FromResult(VerificationVerdict.Rejected(
                $"{request.Platform} artifacts are not verified by the Android verifier."));
        }

        return Task.FromResult(Verify(artifactPath, cancellationToken));
    }

    private VerificationVerdict Verify(string artifactPath, CancellationToken cancellationToken)
    {
        var file = ResolveArtifact(artifactPath);

        if (file is null)
        {
            return VerificationVerdict.Rejected(
                "The build reported success but produced no APK. The build log will say what it did instead.");
        }

        if (file.Length < settings.MinArtifactBytes)
        {
            return VerificationVerdict.Rejected(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The APK is only {file.Length:N0} bytes, which is too small to be a working app. The build reported success without assembling anything."));
        }

        if (file.Length > settings.MaxArtifactBytes)
        {
            return VerificationVerdict.Rejected(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The APK is {file.Length / 1024 / 1024:N0} MB, over the {settings.MaxArtifactBytes / 1024 / 1024:N0} MB limit. Google Play will not accept it."));
        }

        ZipArchive? archive = null;

        try
        {
            try
            {
                archive = ZipFile.OpenRead(file.FullName);
            }
            catch (InvalidDataException exception)
            {
                return VerificationVerdict.Rejected(
                    $"The APK is not a readable archive ({exception.Message}). The build produced a corrupt file.");
            }

            return Inspect(archive, cancellationToken);
        }
        finally
        {
            archive?.Dispose();
        }
    }

    private static VerificationVerdict Inspect(ZipArchive archive, CancellationToken cancellationToken)
    {
        var hasDex = false;
        var hasSignature = false;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            hasDex = hasDex
                || (entry.FullName.StartsWith(DexEntryPrefix, StringComparison.Ordinal)
                    && entry.FullName.EndsWith(".dex", StringComparison.Ordinal));

            hasSignature = hasSignature
                || (entry.FullName.StartsWith("META-INF/", StringComparison.Ordinal)
                    && (entry.FullName.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".EC", StringComparison.OrdinalIgnoreCase)));
        }

        if (archive.GetEntry(ManifestEntry) is null)
        {
            return VerificationVerdict.Rejected(
                $"The APK has no {ManifestEntry}. It is a zip file, but it is not an Android package.");
        }

        if (!hasDex)
        {
            return VerificationVerdict.Rejected(
                "The APK contains no compiled code. The build packaged resources without compiling anything.");
        }

        if (archive.GetEntry(AndroidContentPatcher.ConfigEntryPath) is null)
        {
            // ⚠️ Checked here as well as in the patcher, because this is the one
            // failure that produces an app which installs and launches and is
            // simply blank. A crash gets reported; a blank screen gets blamed on
            // the customer's website.
            return VerificationVerdict.Rejected(
                $"The APK has no {AndroidContentPatcher.ConfigEntryPath}. The app would install and "
                + "then have nothing to show.");
        }

        if (!hasSignature)
        {
            // ⚠️ The v1 signature block. A v2/v3-only APK is legitimate and
            // would be rejected here, which is a deliberate trade for now: every
            // artifact this system produces is v1-signed as well, and an
            // unsigned APK reaching a customer cannot be installed at all.
            return VerificationVerdict.Rejected(
                "The APK carries no signature. It cannot be installed on a device.");
        }

        return VerificationVerdict.Ok;
    }

    /// <summary>
    /// Resolves what the build reported into the actual file.
    /// </summary>
    /// <remarks>
    /// Gradle's output path is a directory holding whatever the variant
    /// produced, so the activity hands over the directory and the artifact is
    /// found inside it. The patch path hands over the file itself.
    /// </remarks>
    private static FileInfo? ResolveArtifact(string artifactPath)
    {
        if (File.Exists(artifactPath))
        {
            return new FileInfo(artifactPath);
        }

        if (!Directory.Exists(artifactPath))
        {
            return null;
        }

        // Ordered, so a directory holding more than one APK resolves the same
        // way every time rather than by whatever the filesystem returns first.
        var candidates = Directory.GetFiles(artifactPath, "*.apk", SearchOption.TopDirectoryOnly);
        Array.Sort(candidates, StringComparer.Ordinal);

        return candidates.Length == 0 ? null : new FileInfo(candidates[0]);
    }
}
