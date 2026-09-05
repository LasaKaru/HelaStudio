using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>
/// Tolerant accessors for a half-typed document.
/// </summary>
/// <remarks>
/// The studio validates on every keystroke, so rules routinely see a link rule
/// with no pattern, a plugin whose settings are still being filled in, or a field
/// of the wrong JSON type. Nothing here may throw; a wrong-typed value is treated
/// as absent, and the schema layer reports it separately.
/// </remarks>
internal static class JsonHelpers
{
    internal static JsonObject Obj(JsonNode? node) => node as JsonObject ?? [];

    internal static JsonArray Arr(JsonNode? node) => node as JsonArray ?? [];

    internal static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    internal static bool? Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    /// <summary>True when a permission value asks for the capability.</summary>
    internal static bool IsRequested(JsonNode? node) =>
        Bool(node) ?? Str(node) is { } text && text != "none";

    /// <summary>Visits every string in a document with its JSON Pointer.</summary>
    internal static void WalkStrings(
        JsonNode? node,
        List<object> path,
        Action<string, string, string> visit,
        string key = "")
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue<string>(out var text):
                visit(JsonPointer.Of([.. path]), key, text);
                return;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    path.Add(i);
                    WalkStrings(array[i], path, visit, key);
                    path.RemoveAt(path.Count - 1);
                }

                return;

            case JsonObject obj:
                foreach (var (childKey, value) in obj)
                {
                    path.Add(childKey);
                    WalkStrings(value, path, visit, childKey);
                    path.RemoveAt(path.Count - 1);
                }

                return;

            default:
                return;
        }
    }
}
