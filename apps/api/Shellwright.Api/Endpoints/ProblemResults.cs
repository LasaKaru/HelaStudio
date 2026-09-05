using System.Collections.Immutable;
using Shellwright.Api.Problems;
using Shellwright.ConfigSchema;

namespace Shellwright.Api.Endpoints;

/// <summary>Turns validation output into the response the studio renders.</summary>
public static class ProblemResults
{
    /// <summary>
    /// Reports every diagnostic, not the first one.
    /// </summary>
    /// <param name="result">The validation result.</param>
    /// <returns>A 422 carrying the full list.</returns>
    /// <remarks>
    /// ⚠️ All of them, always. Returning the first error turns fixing a
    /// configuration into a round trip per mistake, and the person doing it
    /// cannot tell whether they are two errors from done or twenty. The
    /// validator already sorts by path and code, so the list is stable between
    /// calls and the studio's error panel does not jump around while somebody
    /// types.
    /// </remarks>
    public static IResult Invalid(ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ApiProblem.From(
            ApiErrors.ConfigInvalid,
            $"{result.Errors.Length} error(s) must be fixed before this can be saved.",
            new Dictionary<string, object?>
            {
                ["errors"] = Describe(result.Errors),
                ["warnings"] = Describe(result.Warnings),
                ["info"] = Describe(result.Info),
            });
    }

    /// <summary>Flattens diagnostics into the wire shape.</summary>
    /// <param name="diagnostics">The diagnostics to describe.</param>
    /// <returns>Serialisable records.</returns>
    public static IReadOnlyList<DiagnosticResponse> Describe(ImmutableArray<Diagnostic> diagnostics) =>
        [.. diagnostics.Select(x => new DiagnosticResponse(
            x.Code,
            Severity(x.Severity),
            x.Path,
            x.Message,
            x.DocsUrl))];

    /// <summary>
    /// Severity names on the wire, matching what the TypeScript engine emits.
    /// </summary>
    /// <remarks>
    /// A lookup rather than <c>ToLowerInvariant</c>: the two engines share a
    /// fixture corpus that pins these exact strings, so they are a contract
    /// with three known values, not text to be casefolded.
    /// </remarks>
    private static string Severity(ConfigSchema.Severity severity) => severity switch
    {
        ConfigSchema.Severity.Error => "error",
        ConfigSchema.Severity.Warning => "warning",
        _ => "info",
    };
}

/// <summary>One finding, as the API reports it.</summary>
/// <param name="Code">Stable, documented, searchable code.</param>
/// <param name="Severity">Whether it blocks a save.</param>
/// <param name="Path">RFC 6901 JSON Pointer to the offending value.</param>
/// <param name="Message">User-facing text that names the fix.</param>
/// <param name="DocsUrl">Where to read more.</param>
public sealed record DiagnosticResponse(
    string Code,
    string Severity,
    string Path,
    string Message,
    string DocsUrl);
