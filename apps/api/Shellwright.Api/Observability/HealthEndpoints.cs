using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Data;

namespace Shellwright.Api.Observability;

/// <summary>What a dependency check found.</summary>
/// <param name="Name">The dependency.</param>
/// <param name="Healthy">Whether it answered.</param>
/// <param name="Milliseconds">How long it took.</param>
public sealed record DependencyStatus(string Name, bool Healthy, long Milliseconds);

/// <summary>Liveness and readiness.</summary>
/// <remarks>
/// ⚠️ The two are not the same check and must never be wired to the same code.
/// Liveness answers "should this process be restarted"; readiness answers
/// "should traffic be sent to it". A liveness probe that touches the database
/// restarts every healthy instance the moment the database blips, turning a
/// brief outage into a total one.
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>Maps the health endpoints.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health/live", () => TypedResults.Ok(new { status = "ok" }))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithSummary("Whether this process is running.");

        app.MapGet("/health/ready", ReadyAsync)
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithSummary("Whether this process can serve traffic.");

        return app;
    }

    private static async Task<IResult> ReadyAsync(
        ShellwrightDbContext database,
        CancellationToken cancellationToken)
    {
        var checks = new List<DependencyStatus> { await CheckAsync("postgres", Probe, cancellationToken) };

        var healthy = checks.TrueForAll(x => x.Healthy);

        return healthy
            ? TypedResults.Ok(new { status = "ready", checks })

            // 503, so a load balancer takes this instance out of rotation
            // rather than sending it work it cannot do.
            : TypedResults.Json(
                new { status = "not ready", checks },
                statusCode: StatusCodes.Status503ServiceUnavailable);

        async Task Probe(CancellationToken token)
        {
            // ⚠️ Deliberately not a query against a table. This runs as the
            // application role and must stay useful even while row-level
            // security is denying everything — readiness is about the
            // connection, not about what it can see.
            await database.Database.ExecuteSqlRawAsync("SELECT 1", token);
        }
    }

    private static async Task<DependencyStatus> CheckAsync(
        string name,
        Func<CancellationToken, Task> probe,
        CancellationToken cancellationToken)
    {
        // A probe with no deadline is a probe that hangs the health endpoint,
        // which is how an orchestrator ends up unable to tell anything at all.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await probe(deadline.Token);
            return new DependencyStatus(name, true, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // ⚠️ Broad on purpose, and the one place in this codebase where
            // that is right: the answer to "is this dependency reachable" is
            // false for every reason it might not be, and a readiness endpoint
            // that throws tells the orchestrator nothing.
            return new DependencyStatus(name, false, stopwatch.ElapsedMilliseconds);
        }
    }
}
