using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema;

/// <summary>
/// Fills schema defaults into a configuration document.
/// </summary>
/// <remarks>
/// Defaults live in the schema, not in code, so the studio, code generation, and
/// hashing all see the same values. Resolution happens before canonicalisation:
/// an omitted field and an explicitly-default field must hash identically, or a
/// user toggling a value back to its default would miss the build cache.
///
/// This must agree exactly with <c>src/defaults.ts</c>.
/// </remarks>
public static class SchemaDefaults
{
    /// <summary>Returns a copy of <paramref name="value"/> with every schema default filled in.</summary>
    /// <param name="value">The document to resolve. May be null.</param>
    /// <param name="schema">The root JSON Schema.</param>
    /// <returns>The resolved document.</returns>
    public static JsonNode? Resolve(JsonNode? value, JsonObject schema) =>
        ResolveNode(value, schema, schema) ?? value?.DeepClone();

    private static JsonNode? ResolveNode(JsonNode? value, JsonObject rawNode, JsonObject root)
    {
        var node = Deref(rawNode, root);

        if (node["properties"] is JsonObject)
        {
            // A value of the wrong JSON type is left exactly as authored:
            // fabricating defaults over it would hide the error that validation
            // is about to report.
            if (value is not null && value is not JsonObject)
            {
                return value.DeepClone();
            }

            return ResolveObject(value as JsonObject, node, root);
        }

        if (node["items"] is JsonObject itemSchema && value is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
            {
                result.Add(ResolveNode(item, itemSchema, root) ?? item?.DeepClone());
            }

            return result;
        }

        // A union (oneOf/anyOf) is left as authored: picking a branch to fill
        // defaults into would guess at the user's intent.
        return value?.DeepClone() ?? node["default"]?.DeepClone();
    }

    private static JsonObject? ResolveObject(JsonObject? value, JsonObject node, JsonObject root)
    {
        var properties = node["properties"] as JsonObject ?? [];
        var present = value is not null;
        var result = new JsonObject();

        foreach (var (key, childSchema) in properties)
        {
            if (childSchema is not JsonObject child)
            {
                continue;
            }

            var resolved = ResolveNode(value?[key], child, root);
            if (resolved is not null)
            {
                result[key] = resolved;
            }
        }

        // Preserve anything the schema does not model: x- extensions, plugin
        // config bodies, and fields written by a newer studio.
        if (value is not null)
        {
            foreach (var (key, raw) in value)
            {
                if (!properties.ContainsKey(key) && raw is not null)
                {
                    result[key] = raw.DeepClone();
                }
            }
        }

        return !present && result.Count == 0 ? null : result;
    }

    private static JsonObject Deref(JsonObject node, JsonObject root)
    {
        if (node["$ref"]?.GetValue<string>() is not { } reference)
        {
            return node;
        }

        var name = reference.Replace("#/$defs/", string.Empty, StringComparison.Ordinal);
        if (root["$defs"]?[name] is not JsonObject target)
        {
            throw new InvalidOperationException($"Unresolvable schema reference: {reference}");
        }

        // A $ref sibling may carry its own default, which wins over the target's.
        if (node["default"] is not { } ownDefault)
        {
            return target;
        }

        var merged = (JsonObject)target.DeepClone();
        merged["default"] = ownDefault.DeepClone();
        return merged;
    }
}
