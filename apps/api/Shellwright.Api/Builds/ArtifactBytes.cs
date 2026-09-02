using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Shellwright.Api.Data;

namespace Shellwright.Api.Builds;

/// <summary>What a signed link resolves to.</summary>
/// <param name="Reference">The content-addressed artifact reference.</param>
/// <param name="FileName">What the download should be called.</param>
public sealed record ArtifactDescriptor(string Reference, string FileName);

/// <summary>Resolves and reads artifacts for the signed download endpoint.</summary>
/// <remarks>
/// ⚠️ Separate from the normal data access on purpose. The download endpoint is
/// anonymous, so it has no tenant identity to stamp and every row-level security
/// policy hides everything from it. This is the one place in the API that reads
/// a build without a member's identity behind it, and keeping it in its own
/// small interface is what makes that reviewable — rather than a
/// <c>BYPASSRLS</c> connection quietly available to every handler.
/// </remarks>
public interface IArtifactBytes
{
    /// <summary>Finds the artifact a build produced.</summary>
    /// <param name="appId">The app named in the link.</param>
    /// <param name="buildId">The build named in the link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What it produced, or null.</returns>
    Task<ArtifactDescriptor?> FindAsync(Guid appId, Guid buildId, CancellationToken cancellationToken = default);

    /// <summary>Opens the bytes.</summary>
    /// <param name="artifactReference">The reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A stream the caller disposes, or null when the object is gone.</returns>
    Task<Stream?> OpenAsync(string artifactReference, CancellationToken cancellationToken = default);
}

/// <summary>Where finished artifacts are stored.</summary>
public sealed class ArtifactDownloadOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "ArtifactStorage";

    /// <summary>Directory artifacts were written to by the orchestrator.</summary>
    [Required]
    public string Directory { get; set; } = string.Empty;
}

/// <summary>
/// Reads artifacts from the same directory the orchestrator wrote them to.
/// </summary>
/// <remarks>
/// ⚠️ The same gap as asset blob storage: this is a directory, and production
/// wants R2. Recorded in <c>ACTION_REQUIRED.md</c> rather than hidden behind an
/// interface name that implies object storage already exists.
/// </remarks>
/// <param name="database">The database context.</param>
/// <param name="options">Where artifacts are.</param>
public sealed class FileSystemArtifactBytes(
    ShellwrightDbContext database,
    IOptions<ArtifactDownloadOptions> options) : IArtifactBytes
{
    private const string ReferenceScheme = "artifact://sha256-";

    private readonly ArtifactDownloadOptions settings =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<ArtifactDescriptor?> FindAsync(
        Guid appId,
        Guid buildId,
        CancellationToken cancellationToken = default)
    {
        // ⚠️ Through app_artifact_for_download, a SECURITY DEFINER function that
        // answers exactly this question and nothing else. The endpoint above is
        // anonymous, so no identity is stamped and every policy correctly hides
        // every row from it; the alternatives were giving the API a
        // policy-bypassing connection that every other handler could reach, or
        // handing it the orchestrator's role. Data/Sql/ArtifactDownload.up.sql
        // sets out why neither is acceptable.
        //
        // Both identifiers are passed, so a link cannot be replayed against a
        // different app.
        var connection = database.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await database.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = new NpgsqlCommand(
            "SELECT artifact_reference, app_name, platform FROM app_artifact_for_download(@app, @build)",
            (NpgsqlConnection)connection);

        await using (command.ConfigureAwait(false))
        {
            command.Parameters.AddWithValue("build", buildId);
            command.Parameters.AddWithValue("app", appId);

            var reader = await command.ExecuteReaderAsync(cancellationToken);
            await using (reader.ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                var extension = reader.GetInt32(2) == 0 ? "apk" : "ipa";

                return new ArtifactDescriptor(
                    reader.GetString(0),
                    $"{Slug.From(reader.GetString(1))}.{extension}");
            }
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenAsync(string artifactReference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactReference);

        if (!artifactReference.StartsWith(ReferenceScheme, StringComparison.Ordinal))
        {
            return Task.FromResult<Stream?>(null);
        }

        var digest = artifactReference[ReferenceScheme.Length..];

        // ⚠️ Validated before it becomes a path. The reference comes from a
        // database row, and a row containing separators would otherwise read
        // whatever the API process can read.
        if (digest.Length != 64 || !digest.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
        {
            return Task.FromResult<Stream?>(null);
        }

        var path = Path.Combine(settings.Directory, digest[..2], digest[2..4], digest);

        return Task.FromResult<Stream?>(
            File.Exists(path)
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true)
                : null);
    }
}
