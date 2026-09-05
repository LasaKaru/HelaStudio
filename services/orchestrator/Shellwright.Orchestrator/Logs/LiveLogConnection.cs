using StackExchange.Redis;

namespace Shellwright.Orchestrator.Logs;

/// <summary>
/// The Redis connection the live log stream uses, if there is one.
/// </summary>
/// <remarks>
/// ⚠️ A holder rather than the connection itself, because "no Redis" is a
/// supported configuration and the container cannot register a null service.
/// Making the absence explicit in a type also means every consumer has to
/// decide what it does without one, instead of discovering the answer as a
/// <see cref="NullReferenceException"/> during someone's build.
/// </remarks>
/// <param name="Connection">The connection, or null when only archiving.</param>
public sealed record LiveLogConnection(IConnectionMultiplexer? Connection) : IAsyncDisposable
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
        {
            await Connection.DisposeAsync();
        }
    }
}
