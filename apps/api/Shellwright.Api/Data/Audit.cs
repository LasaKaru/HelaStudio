using System.Text.Json.Nodes;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Data;

/// <summary>One entry for the customer-visible audit trail.</summary>
/// <param name="OrgId">Organisation the action happened in.</param>
/// <param name="ActorId">Who did it, or null for a system action.</param>
/// <param name="Action">Dotted action name, such as <c>config.version.created</c>.</param>
/// <param name="SubjectType">Type of the thing acted on.</param>
/// <param name="SubjectId">Identifier of the thing acted on.</param>
/// <param name="Meta">
/// Structured detail. ⚠️ Never a secret, and never a whole config body — this
/// is read by support staff and exported to customers.
/// </param>
public sealed record AuditEntry(
    Guid OrgId,
    Guid? ActorId,
    string Action,
    string SubjectType,
    Guid SubjectId,
    IReadOnlyDictionary<string, string>? Meta = null);

/// <summary>Appends to the customer-visible audit trail.</summary>
public static class Audit
{
    /// <summary>Records that something happened.</summary>
    /// <param name="database">The database context.</param>
    /// <param name="entry">What happened.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the row is saved.</returns>
    public static async Task WriteAsync(
        ShellwrightDbContext database,
        AuditEntry entry,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(clock);

        JsonObject? payload = null;
        if (entry.Meta is { Count: > 0 })
        {
            payload = [];
            foreach (var (key, value) in entry.Meta)
            {
                payload[key] = value;
            }
        }

        database.AuditEvents.Add(new AuditEvent
        {
            OrgId = entry.OrgId,
            ActorId = entry.ActorId,
            Action = entry.Action,
            SubjectType = entry.SubjectType,
            SubjectId = entry.SubjectId.ToString(),
            Meta = payload,
            At = clock.GetUtcNow(),
        });

        await database.SaveChangesAsync(cancellationToken);
    }
}
