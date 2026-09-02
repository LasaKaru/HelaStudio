using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Shellwright.Api.Observability;

/// <summary>Counts the queries one request issued.</summary>
public sealed class QueryCounter
{
    /// <summary>How many commands have been executed on this request's contexts.</summary>
    public int Count { get; private set; }

    /// <summary>Records one command.</summary>
    public void Increment() => Count++;
}

/// <summary>
/// Detects N+1 query patterns.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ A warning, plus a test assertion, rather than a hard failure at runtime.
/// The failure mode this catches is a loop that issues a query per row: correct,
/// fast on the developer's ten rows, and unusable on a customer's ten thousand.
/// Nothing else notices it — every test passes, no error is logged, and the
/// first symptom is a timeout in production.
/// </para>
/// <para>
/// The threshold is per request rather than per query, because the shape being
/// looked for is a count that grows with the data rather than any individual
/// statement being slow.
/// </para>
/// </remarks>
/// <param name="counter">The current request's counter.</param>
/// <param name="logger">Where the warning goes.</param>
public sealed class QueryCountInterceptor(QueryCounter counter, ILogger<QueryCountInterceptor> logger)
    : DbCommandInterceptor
{
    /// <summary>Queries in one request beyond which something is probably looping.</summary>
    public const int WarnAbove = 20;

    /// <inheritdoc />
    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Record();
        return base.ReaderExecuted(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record();
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Record();
        return base.ScalarExecuted(command, eventData, result);
    }

    /// <inheritdoc />
    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Record();
        return base.NonQueryExecuted(command, eventData, result);
    }

    private void Record()
    {
        counter.Increment();

        // Logged once, on the crossing, rather than on every query past the
        // threshold — a request that issues four hundred would otherwise
        // produce three hundred and eighty warnings about itself.
        if (counter.Count == WarnAbove + 1)
        {
            logger.LogWarning(
                "This request has issued more than {Threshold} database queries. "
                + "That is the shape of an N+1: a query inside a loop over rows.",
                WarnAbove);
        }
    }
}
