using System.Text.Json.Nodes;
using Shellwright.ConfigSchema;

namespace Shellwright.Api.Config;

/// <summary>What changed at one location.</summary>
/// <param name="Path">RFC 6901 JSON Pointer to the value.</param>
/// <param name="Kind">One of <c>added</c>, <c>removed</c>, or <c>changed</c>.</param>
/// <param name="From">The previous value, canonicalised, or null when added.</param>
/// <param name="To">The new value, canonicalised, or null when removed.</param>
public sealed record ConfigChange(string Path, string Kind, string? From, string? To);

/// <summary>
/// Compares two configurations.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Values are compared as canonical JSON, not with <c>JsonNode.Equals</c>,
/// which is reference equality and would report every value as changed. The
/// canonical form also settles the two cases a naive comparison gets wrong:
/// key order, which carries no meaning, and number formatting, where
/// <c>1</c> and <c>1.0</c> are the same value written differently.
/// </para>
/// <para>
/// Arrays are compared as whole values rather than element by element. A
/// longest-common-subsequence diff would produce a prettier result for a
/// reordered tab bar, and it would also mean the diff and the cache key
/// disagree about what changed — the hashes treat an array as one value, so
/// this does too.
/// </para>
/// </remarks>
public static class ConfigDiff
{
    /// <summary>Computes the changes between two documents.</summary>
    /// <param name="from">The earlier document.</param>
    /// <param name="to">The later document.</param>
    /// <returns>Changes, ordered by path.</returns>
    public static IReadOnlyList<ConfigChange> Between(JsonObject? from, JsonObject? to)
    {
        var changes = new List<ConfigChange>();
        Compare(from, to, [], changes);

        return [.. changes.OrderBy(x => x.Path, StringComparer.Ordinal)];
    }

    private static void Compare(JsonNode? from, JsonNode? to, List<object> path, List<ConfigChange> changes)
    {
        if (from is JsonObject left && to is JsonObject right)
        {
            foreach (var key in left.Select(x => x.Key).Union(right.Select(x => x.Key), StringComparer.Ordinal))
            {
                path.Add(key);
                Compare(left[key], right[key], path, changes);
                path.RemoveAt(path.Count - 1);
            }

            return;
        }

        var before = from is null ? null : CanonicalJson.Serialize(from);
        var after = to is null ? null : CanonicalJson.Serialize(to);

        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        var kind = (before, after) switch
        {
            (null, not null) => "added",
            (not null, null) => "removed",
            _ => "changed",
        };

        changes.Add(new ConfigChange(JsonPointer.Of([.. path]), kind, before, after));
    }
}
