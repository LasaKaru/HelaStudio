using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Verification;

/// <summary>
/// Sends each artifact to the verifier that understands its format.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This type exists because the alternative was silent. Before it, one
/// verifier was registered for every build, so an IPA was checked by a verifier
/// looking for <c>AndroidManifest.xml</c> and <c>classes.dex</c>. That happened
/// to fail closed, which is the only reason it would not have shipped an
/// unverified binary — but it would have rejected every iOS build with a reason
/// naming Android, and the person reading it would have had no way to tell a
/// broken build from a broken check.
/// </para>
/// <para>
/// ⚠️ No default arm. A platform with no verifier is rejected by name. A
/// dispatcher whose unknown case passed would be worse than having no
/// dispatcher: it would report "verified" for something nothing inspected.
/// </para>
/// </remarks>
/// <param name="android">Checks APKs.</param>
/// <param name="ios">Checks IPAs.</param>
public sealed class PlatformArtifactVerifier(
    AndroidArtifactVerifier android,
    IosArtifactVerifier ios) : IArtifactVerifier
{
    private readonly AndroidArtifactVerifier android =
        android ?? throw new ArgumentNullException(nameof(android));

    private readonly IosArtifactVerifier ios = ios ?? throw new ArgumentNullException(nameof(ios));

    /// <inheritdoc />
    public Task<VerificationVerdict> VerifyAsync(
        BuildRequest request,
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Platform switch
        {
            BuildPlatform.Android => android.VerifyAsync(request, artifactPath, cancellationToken),
            BuildPlatform.Ios => ios.VerifyAsync(request, artifactPath, cancellationToken),
            _ => Task.FromResult(VerificationVerdict.Rejected(
                $"Nothing knows how to verify a {request.Platform} artifact, so it cannot be released.")),
        };
    }
}
