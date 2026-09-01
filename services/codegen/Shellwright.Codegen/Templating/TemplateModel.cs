using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban.Runtime;
using Shellwright.ConfigSchema;

namespace Shellwright.Codegen.Templating;

/// <summary>
/// Turns a resolved configuration into the object a template sees.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The important decision here: <b>every string in the model is already
/// escaped for the file being written</b>. A template author writes
/// <c>{{ app.name }}</c> and cannot get the escaping wrong, because there is no
/// unescaped value to reach for.
/// </para>
/// <para>
/// The obvious alternative — helper functions like <c>{{ android_string
/// app.name }}</c> — was rejected. It works exactly as long as everyone
/// remembers, and the failure when someone forgets is invisible: the project
/// generates, the golden file records the wrong bytes, and the build breaks
/// only for the customers whose app name contains an apostrophe. Making the
/// safe thing the only thing costs one class and removes a whole category of
/// bug.
/// </para>
/// <para>
/// The genuinely raw values are still reachable under <c>raw</c>, for the two
/// cases that need them: embedding pre-serialised canonical JSON, and computing
/// a hash. Anything else using <c>raw</c> should be treated as a bug in review.
/// </para>
/// </remarks>
public static class TemplateModel
{
    /// <summary>Builds the model for one output format.</summary>
    /// <param name="resolved">A configuration with schema defaults applied.</param>
    /// <param name="format">The format every string will be escaped for.</param>
    /// <param name="extras">Values from outside the config, such as toolchain versions.</param>
    /// <returns>A Scriban object ready to render.</returns>
    public static ScriptObject Build(
        JsonObject resolved,
        TemplateFormat format,
        IReadOnlyDictionary<string, object?>? extras = null)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var model = (ScriptObject)Convert(resolved, format)!;
        model["raw"] = Convert(resolved, TemplateFormat.None);

        foreach (var (key, value) in (extras ?? new Dictionary<string, object?>())
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            model[key] = EscapeExtra(value, format);
        }

        return model;
    }

    /// <summary>
    /// Applies the same escaping to values that come from outside the config.
    /// </summary>
    /// <remarks>
    /// Locale codes, hostnames and permission names are all schema-validated,
    /// so none of them can currently carry a character that needs escaping.
    /// They are escaped anyway: the guarantee this class offers is "everything
    /// a template can reach is safe", and an exception for values that happen
    /// to be safe today is how that guarantee quietly stops being true.
    /// </remarks>
    private static object? EscapeExtra(object? value, TemplateFormat format) => value switch
    {
        string text => Escapers.Escape(text, format),
        IEnumerable<string> items => items.Select(item => Escapers.Escape(item, format)).ToList(),
        _ => value,
    };

    private static object? Convert(JsonNode? node, TemplateFormat format)
    {
        switch (node)
        {
            case null:
                return null;

            case JsonObject obj:
                {
                    var script = new ScriptObject();

                    // Ordered so that anything a template does with the object as a
                    // whole — iterating it, or a future rule emitting all of it —
                    // cannot depend on hash-table order.
                    foreach (var (key, value) in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        script[key] = Convert(value, format);
                    }

                    return script;
                }

            case JsonArray array:
                return array.Select(item => Convert(item, format)).ToList();

            case JsonValue value:
                return ConvertScalar(value, format);

            default:
                throw new InvalidOperationException($"Unsupported JSON node: {node.GetType().Name}");
        }
    }

    /// <summary>
    /// Converts one scalar.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <see cref="JsonValue"/> holds its payload one of two ways: a node
    /// parsed from text wraps a <see cref="JsonElement"/>, while a node built
    /// in code wraps a CLR value directly. Only the parsed form can be read as
    /// a <see cref="JsonElement"/>. Reading only that form works for every
    /// fixture — they all come from files — and throws the moment the API
    /// generates from a config it assembled in memory, which is what it will
    /// do for every real customer. <see cref="CanonicalJson"/> already handles
    /// both; this now does too.
    /// </remarks>
    private static object? ConvertScalar(JsonValue value, TemplateFormat format)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => Escapers.Escape(element.GetString()!, format),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,

                // The canonical number formatter rather than ToString, so 1.0
                // renders as "1" in a generated file exactly as it does in a
                // hashed config, on every machine and in every locale.
                JsonValueKind.Number => CanonicalJson.FormatNumber(element.GetDouble()),

                _ => throw new InvalidOperationException(
                    string.Create(CultureInfo.InvariantCulture, $"Unsupported scalar: {element.ValueKind}")),
            };
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return Escapers.Escape(text, format);
        }

        if (value.TryGetValue<double>(out var number))
        {
            return CanonicalJson.FormatNumber(number);
        }

        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"Unsupported scalar node: {value.GetValueKind()}"));
    }
}
