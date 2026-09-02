using Microsoft.AspNetCore.Mvc;

namespace Shellwright.Api.Problems;

/// <summary>Builds RFC 9457 responses from the error catalogue.</summary>
public static class ApiProblem
{
    /// <summary>Returns a problem response for a catalogued error.</summary>
    /// <param name="error">The error.</param>
    /// <param name="detail">Human-readable specifics. ⚠️ Never a secret, and never a stack trace.</param>
    /// <param name="extensions">Extra members, such as a diagnostic list.</param>
    /// <returns>The result.</returns>
    public static IResult From(
        ApiError error,
        string? detail = null,
        IDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return TypedResults.Problem(new ProblemDetails
        {
            Type = error.Type,
            Title = error.Title,
            Status = error.Status,
            Detail = detail,
            Extensions = Merge(error, extensions),
        });
    }

    /// <summary>Returns a validation problem carrying per-field messages.</summary>
    /// <param name="errors">Messages keyed by field name.</param>
    /// <returns>The result.</returns>
    public static IResult Validation(IDictionary<string, string[]> errors)
    {
        var error = ApiErrors.ValidationFailed;

        return TypedResults.Problem(new ValidationProblemDetails(errors)
        {
            Type = error.Type,
            Title = error.Title,

            // ⚠️ 422, not the framework's default 400. A body that parsed and
            // then failed a rule is a different thing from one that did not
            // parse, and a client retrying blindly needs to be able to tell
            // them apart.
            Status = error.Status,
            Extensions = Merge(error, null),
        });
    }

    private static Dictionary<string, object?> Merge(ApiError error, IDictionary<string, object?>? extensions)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            // The catalogue code, alongside the type URI it derives from.
            // Clients branch on this; the URI is for the person reading it.
            ["code"] = error.Code,
        };

        if (extensions is null)
        {
            return merged;
        }

        foreach (var (key, value) in extensions)
        {
            merged[key] = value;
        }

        return merged;
    }
}
