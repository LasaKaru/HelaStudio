using System.Collections.Immutable;
using StackExchange.Redis;

namespace Shellwright.Orchestrator.Logs;

/// <summary>One line as it was read back from the live stream.</summary>
/// <param name="StreamId">
/// Where this line sits in the stream. A viewer that reconnects passes the last
/// one it saw back to <see cref="BuildLogReader.ReadAsync"/> and carries on
/// from there rather than from the beginning.
/// </param>
/// <param name="Line">The line itself.</param>
public sealed record StreamedLogLine(string StreamId, LogLine Line);

/// <summary>
/// A page of the live log, and where to resume from.
/// </summary>
/// <param name="Lines">The lines, oldest first.</param>
/// <param name="LastStreamId">
/// The id to pass as <c>afterStreamId</c> next time. When the page is empty
/// this is the id that was asked for, so a caller that polls an idle build does
/// not rewind.
/// </param>
public sealed record LiveLogPage(ImmutableArray<StreamedLogLine> Lines, string LastStreamId);

/// <summary>
/// Reads a build's live log back out of Redis.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The live stream is not the record. It is bounded (see
/// <see cref="LogPipelineOptions.LiveStreamLines"/>), it is trimmed
/// approximately, and it is abandoned outright if Redis fails mid-build — so a
/// page read from here can be missing its oldest lines, and a caller that needs
/// the whole log must read the archive instead. What this class is for is the
/// person watching a build happen.
/// </para>
/// <para>
/// ⚠️ Reads are explicitly paged rather than blocking. A fan-out that held a
/// blocking read per viewer would hold a Redis connection per viewer, and the
/// managed tiers we run on count connections.
/// </para>
/// </remarks>
/// <param name="redis">Connection to Redis.</param>
public sealed class BuildLogReader(IConnectionMultiplexer redis)
{
    /// <summary>The position meaning "from the very start of the stream".</summary>
    public const string Beginning = "0-0";

    /// <summary>Reads the lines added after a position.</summary>
    /// <param name="buildId">Which build.</param>
    /// <param name="afterStreamId">
    /// The last id already seen, or <see cref="Beginning"/> to start at the
    /// oldest line the stream still holds.
    /// </param>
    /// <param name="count">The most lines to return.</param>
    /// <returns>The lines, and the id to resume from.</returns>
    public async Task<LiveLogPage> ReadAsync(Guid buildId, string afterStreamId, int count = 500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(afterStreamId);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var entries = await redis.GetDatabase().StreamReadAsync(
            RedisLogPipeline.StreamKey(buildId),
            afterStreamId,
            count);

        if (entries.Length == 0)
        {
            return new LiveLogPage([], afterStreamId);
        }

        var lines = ImmutableArray.CreateBuilder<StreamedLogLine>(entries.Length);

        foreach (var entry in entries)
        {
            lines.Add(new StreamedLogLine(entry.Id.ToString(), ToLine(entry)));
        }

        return new LiveLogPage(lines.MoveToImmutable(), entries[^1].Id.ToString());
    }

    private static LogLine ToLine(StreamEntry entry)
    {
        string text = string.Empty;
        var severity = LogSeverity.Info;
        var redacted = false;

        foreach (var field in entry.Values)
        {
            // ⚠️ Field by field rather than by position. Redis preserves the
            // order we wrote in, but a stream written by an older build of this
            // service is still in the same stream, and reading by position
            // would silently mis-assign its fields.
            switch (field.Name.ToString())
            {
                case "text":
                    text = field.Value.ToString();
                    break;

                case "severity":
                    severity = Enum.TryParse<LogSeverity>(field.Value.ToString(), out var parsed)
                        ? parsed
                        : LogSeverity.Info;
                    break;

                case "redacted":
                    redacted = bool.TryParse(field.Value.ToString(), out var flag) && flag;
                    break;

                default:
                    break;
            }
        }

        return new LogLine(text, severity, redacted);
    }
}
