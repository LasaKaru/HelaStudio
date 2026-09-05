using System.Diagnostics;

namespace Shellwright.Api.Observability;

/// <summary>
/// Gives every request an identifier that appears in its logs, its traces, and
/// its error responses.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Added now rather than when it is first needed. Retrofitting correlation
/// across an asynchronous workflow system — a config save that triggers a build
/// that produces an artifact that gets submitted — means going back through
/// every hop, and by then the hops that most need it are the ones nobody
/// remembers the shape of.
/// </para>
/// <para>
/// An inbound identifier is honoured so a caller can stitch its own logs to
/// ours, but it is bounded and filtered first: it goes into log output and into
/// a response header, and an unbounded caller-controlled string in either place
/// is a log-injection and header-splitting sink.
/// </para>
/// </remarks>
/// <param name="next">The rest of the pipeline.</param>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    /// <summary>Header the identifier travels in, both ways.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Longest identifier accepted from a caller.</summary>
    private const int MaxLength = 64;

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task for the rest of the pipeline.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Sanitise(context.Request.Headers[HeaderName].ToString())
            ?? Activity.Current?.TraceId.ToString()
            ?? context.TraceIdentifier;

        context.Items[HeaderName] = correlationId;
        Activity.Current?.SetTag("shellwright.correlation_id", correlationId);

        // Set before the response starts, because headers cannot be added once
        // the first byte is on the wire — and the responses most worth
        // correlating are the ones that fail partway through.
        context.Response.Headers[HeaderName] = correlationId;

        await next(context);
    }

    /// <summary>
    /// Accepts a caller-supplied identifier, or nothing.
    /// </summary>
    /// <param name="candidate">The header value as received.</param>
    /// <returns>A safe identifier, or null to generate one instead.</returns>
    /// <remarks>
    /// Restricted to characters that cannot terminate a header or forge a log
    /// line. Anything else is discarded silently rather than rejected: a
    /// malformed correlation id is not worth failing a request over, and
    /// generating one loses nothing but the caller's ability to stitch.
    /// </remarks>
    private static string? Sanitise(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return null;
        }

        foreach (var c in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))
            {
                return null;
            }
        }

        return candidate;
    }
}
