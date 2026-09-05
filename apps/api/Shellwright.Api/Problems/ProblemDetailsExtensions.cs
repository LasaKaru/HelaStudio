using Microsoft.AspNetCore.Diagnostics;
using Shellwright.Api.Observability;

namespace Shellwright.Api.Problems;

/// <summary>Registers the problem-details pipeline.</summary>
public static class ProblemDetailsExtensions
{
    /// <summary>Adds RFC 9457 responses and the handler that turns exceptions into them.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Put on every problem response, including the ones the
                // framework generates for a 404 or a 405 that never reached our
                // code. It is the one thing a customer can quote that lets
                // somebody find the request in the logs.
                if (context.HttpContext.Items.TryGetValue(CorrelationMiddleware.HeaderName, out var correlationId))
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }

                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
            });

        services.AddExceptionHandler<UnhandledExceptionHandler>();

        return services;
    }
}

/// <summary>
/// Turns an unhandled exception into a problem response that says nothing
/// useful to an attacker and everything useful to support.
/// </summary>
/// <remarks>
/// ⚠️ The message and the stack trace go to the log, never to the response.
/// A .NET exception message routinely contains a connection string, a file
/// path, or a fragment of a query — all of which are a map of the
/// infrastructure. What the caller gets is a correlation id, which is enough
/// for somebody with log access to find the whole thing.
/// </remarks>
/// <param name="logger">Where the detail goes.</param>
public sealed class UnhandledExceptionHandler(ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var correlationId = httpContext.Items.TryGetValue(CorrelationMiddleware.HeaderName, out var value)
            ? value?.ToString()
            : httpContext.TraceIdentifier;

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}. Correlation {CorrelationId}.",
            httpContext.Request.Method,
            httpContext.Request.Path,
            correlationId);

        await ApiProblem
            .From(
                ApiErrors.Internal,
                "Something went wrong on our side. Quote the correlation id if you get in touch.")
            .ExecuteAsync(httpContext);

        return true;
    }
}
