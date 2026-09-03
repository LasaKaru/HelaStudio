using System.Globalization;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Verification;

/// <summary>
/// Checks an iOS artifact before anybody is allowed to download it.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Structural, like the Android verifier, and for the same reason: it is the
/// check that can run anywhere. Whether the signature is valid and whether the
/// embedded profile matches the bundle id are questions for <c>codesign</c> and
/// <c>security</c>, which exist only on the macOS runner.
/// </para>
/// <para>
/// ⚠️ Every rule is a way an iOS build succeeds and produces something
/// unusable. An IPA with no embedded provisioning profile installs from Xcode
/// on the developer's own machine and fails for every other person with an
/// error that names nothing. One whose <c>Payload</c> holds no <c>.app</c> is a
/// zip that TestFlight rejects after the upload has finished.
/// </para>
/// </remarks>
/// <param name="options">Verification settings.</param>
public sealed class IosArtifactVerifier(IOptions<VerificationOptions> options) : IArtifactVerifier
{
    private const string PayloadPrefix = "Payload/";

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

        if (request.Platform != BuildPlatform.Ios)
        {
            // Rejected rather than passed, for the same reason as the Android
            // verifier: a check that says "fine" for what it cannot inspect is
            // worse than no check.
            return Task.FromResult(VerificationVerdict.Rejected(
                $"{request.Platform} artifacts are not verified by the iOS verifier."));
        }

        return Task.FromResult(Verify(artifactPath, cancellationToken));
    }

    private VerificationVerdict Verify(string artifactPath, CancellationToken cancellationToken)
    {
        var file = ResolveArtifact(artifactPath);

        if (file is null)
        {
            return VerificationVerdict.Rejected(
                "The build reported success but produced no IPA. The build log will say what it did instead.");
        }

        if (file.Length < settings.MinArtifactBytes)
        {
            return VerificationVerdict.Rejected(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The IPA is only {file.Length:N0} bytes, which is too small to be a working app. The build reported success without archiving anything."));
        }

        if (file.Length > settings.MaxArtifactBytes)
        {
            return VerificationVerdict.Rejected(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The IPA is {file.Length / 1024 / 1024:N0} MB, over the {settings.MaxArtifactBytes / 1024 / 1024:N0} MB limit."));
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
                    $"The IPA is not a readable archive ({exception.Message}). The build produced a corrupt file.");
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
        string? appBundle = null;
        var hasInfoPlist = false;
        var hasProfile = false;
        var hasExecutable = false;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!entry.FullName.StartsWith(PayloadPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var relative = entry.FullName[PayloadPrefix.Length..];
            var separator = relative.IndexOf('/', StringComparison.Ordinal);

            if (separator <= 0)
            {
                continue;
            }

            var bundle = relative[..separator];

            if (!bundle.EndsWith(".app", StringComparison.Ordinal))
            {
                continue;
            }

            // ⚠️ One app bundle, not several. An IPA carrying two would be
            // accepted by the archive format and rejected by App Store Connect
            // after the customer had waited for the upload.
            if (appBundle is not null && !string.Equals(appBundle, bundle, StringComparison.Ordinal))
            {
                return VerificationVerdict.Rejected(
                    $"The IPA contains more than one app bundle ({appBundle} and {bundle}).");
            }

            appBundle = bundle;
            var inside = relative[(separator + 1)..];

            hasInfoPlist = hasInfoPlist || inside == "Info.plist";
            hasProfile = hasProfile || inside == "embedded.mobileprovision";

            // The executable has no extension and sits at the bundle root.
            hasExecutable = hasExecutable
                || (!inside.Contains('/', StringComparison.Ordinal)
                    && !Path.HasExtension(inside)
                    && entry.Length > 0);
        }

        if (appBundle is null)
        {
            return VerificationVerdict.Rejected(
                "The IPA has no Payload/*.app. It is a zip file, but it is not an iOS application archive.");
        }

        if (!hasInfoPlist)
        {
            return VerificationVerdict.Rejected(
                $"{appBundle} has no Info.plist. Nothing would be able to read the app's identity or version.");
        }

        if (!hasExecutable)
        {
            return VerificationVerdict.Rejected(
                $"{appBundle} contains no executable. The build packaged resources without linking anything.");
        }

        if (!hasProfile)
        {
            // ⚠️ The failure that only shows up on somebody else's phone. An
            // IPA with no embedded profile installs from Xcode on the machine
            // that built it and fails everywhere else, with an error naming
            // nothing a customer can act on.
            return VerificationVerdict.Rejected(
                $"{appBundle} has no embedded.mobileprovision. It would install on the build machine and nowhere else.");
        }

        return VerificationVerdict.Ok;
    }

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

        // xcodebuild -exportArchive writes into a directory; the IPA is named
        // after the scheme, which we do not want to depend on here.
        var candidates = Directory.GetFiles(artifactPath, "*.ipa", SearchOption.TopDirectoryOnly);
        Array.Sort(candidates, StringComparer.Ordinal);

        return candidates.Length == 0 ? null : new FileInfo(candidates[0]);
    }
}
