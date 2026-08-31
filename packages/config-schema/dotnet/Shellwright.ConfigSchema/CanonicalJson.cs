using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema;

/// <summary>
/// Canonical JSON — deterministic bytes for a configuration document.
/// </summary>
/// <remarks>
/// <para>
/// This must agree byte for byte with the TypeScript implementation in
/// <c>src/canonical.ts</c>. The cross-language contract test in
/// <c>Shellwright.ConfigSchema.Tests</c> asserts exactly that against the shared
/// fixture corpus, because a disagreement here shows up much later as an
/// unexplainable build-cache miss.
/// </para>
/// <para>
/// Rules: keys sorted by UTF-16 code unit; no insignificant whitespace; numbers
/// in shortest round-trip form; strings NFC-normalised and minimally escaped;
/// explicit nulls omitted from objects; array order preserved.
/// </para>
/// </remarks>
public static class CanonicalJson
{
    /// <summary>Serialises a node to canonical JSON.</summary>
    /// <param name="node">The document to serialise. May be null.</param>
    /// <returns>The canonical JSON text.</returns>
    public static string Serialize(JsonNode? node)
    {
        var builder = new StringBuilder();
        Write(node, builder);
        return builder.ToString();
    }

    /// <summary>Serialises a node to canonical JSON as UTF-8 bytes, ready for hashing.</summary>
    /// <param name="node">The document to serialise. May be null.</param>
    /// <returns>The canonical JSON encoded as UTF-8.</returns>
    public static byte[] SerializeToUtf8(JsonNode? node) => Encoding.UTF8.GetBytes(Serialize(node));

    private static void Write(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                return;
            case JsonArray array:
                WriteArray(array, builder);
                return;
            case JsonObject obj:
                WriteObject(obj, builder);
                return;
            case JsonValue value:
                WriteValue(value, builder);
                return;
            default:
                throw new InvalidOperationException($"Unsupported JSON node type: {node.GetType().Name}");
        }
    }

    private static void WriteArray(JsonArray array, StringBuilder builder)
    {
        builder.Append('[');
        for (var i = 0; i < array.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            // Order is semantic, so an explicit null inside an array is preserved:
            // dropping it would shift every later index.
            Write(array[i], builder);
        }

        builder.Append(']');
    }

    private static void WriteObject(JsonObject obj, StringBuilder builder)
    {
        // Null-valued keys are dropped before sorting, so {"a":null} and {} agree.
        var keys = obj
            .Where(pair => pair.Value is not null)
            .Select(pair => pair.Key)
            .ToList();

        // StringComparer.Ordinal compares by UTF-16 code unit, matching the
        // JavaScript side's `a < b` comparison.
        keys.Sort(StringComparer.Ordinal);

        builder.Append('{');
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            WriteString(keys[i], builder);
            builder.Append(':');
            Write(obj[keys[i]], builder);
        }

        builder.Append('}');
    }

    /// <summary>
    /// Writes a scalar.
    /// </summary>
    /// <remarks>
    /// A <see cref="JsonValue"/> holds its payload one of two ways: a node parsed
    /// from text wraps a <see cref="JsonElement"/>, while a node built in code
    /// wraps a CLR value directly. Only the parsed form can be read as a
    /// <see cref="JsonElement"/>, so both are handled — the projections in
    /// <see cref="ConfigHasher"/> build nodes in code and would otherwise throw.
    /// </remarks>
    private static void WriteValue(JsonValue value, StringBuilder builder)
    {
        if (value.TryGetValue<JsonElement>(out var element))
        {
            WriteElement(element, builder);
            return;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            builder.Append(flag ? "true" : "false");
            return;
        }

        if (value.TryGetValue<string>(out var text))
        {
            WriteString(text, builder);
            return;
        }

        builder.Append(FormatNumber(ReadNumber(value)));
    }

    private static void WriteElement(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                builder.Append("true");
                return;
            case JsonValueKind.False:
                builder.Append("false");
                return;
            case JsonValueKind.Null:
                builder.Append("null");
                return;
            case JsonValueKind.String:
                WriteString(element.GetString() ?? string.Empty, builder);
                return;
            case JsonValueKind.Number:
                builder.Append(FormatNumber(element.GetDouble()));
                return;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}");
        }
    }

    private static double ReadNumber(JsonValue value)
    {
        if (value.TryGetValue<double>(out var asDouble))
        {
            return asDouble;
        }

        if (value.TryGetValue<int>(out var asInt))
        {
            return asInt;
        }

        if (value.TryGetValue<long>(out var asLong))
        {
            return asLong;
        }

        if (value.TryGetValue<decimal>(out var asDecimal))
        {
            return (double)asDecimal;
        }

        throw new InvalidOperationException(
            $"Unsupported JSON scalar of CLR type {value.GetValueKind()}.");
    }

    /// <summary>Formats a number in shortest round-trip form, matching JavaScript.</summary>
    /// <param name="number">The value to format.</param>
    /// <returns>The canonical text for the number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the value is not finite.</exception>
    public static string FormatNumber(double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                "Cannot canonicalise a non-finite number: JSON has no representation for it.");
        }

        if (number == 0)
        {
            // Collapses negative zero, as JavaScript's Object.is(-0) branch does.
            return "0";
        }

        // "R" is shortest round-trip. JavaScript switches to exponent notation at
        // 1e21 and at 1e-7; .NET switches at different thresholds, so the two are
        // reconciled explicitly below.
        var absolute = Math.Abs(number);
        if (absolute >= 1e21 || absolute < 1e-6)
        {
            return FormatExponential(number);
        }

        var plain = number.ToString("R", CultureInfo.InvariantCulture);
        return plain.Contains('E', StringComparison.Ordinal) ? ExpandExponential(plain) : plain;
    }

    private static string FormatExponential(double number)
    {
        var text = number.ToString("R", CultureInfo.InvariantCulture);
        if (!text.Contains('E', StringComparison.Ordinal))
        {
            // .NET wrote it in plain form where JavaScript would not; convert.
            text = number.ToString("E16", CultureInfo.InvariantCulture);
            text = TrimExponentialMantissa(text);
        }

        var parts = text.Split('E');
        var mantissa = parts[0].TrimEnd('.');
        var exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return $"{mantissa}E{exponent.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string TrimExponentialMantissa(string text)
    {
        var parts = text.Split('E');
        var mantissa = parts[0].TrimEnd('0').TrimEnd('.');
        return $"{mantissa}E{parts[1]}";
    }

    private static string ExpandExponential(string text)
    {
        var parts = text.Split('E');
        var exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);
        return $"{parts[0]}E{exponent.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Serialises a string: NFC-normalised, minimally escaped, double-quoted.</summary>
    /// <param name="value">The string to serialise.</param>
    /// <returns>The canonical, quoted form.</returns>
    public static string EscapeString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder();
        WriteString(value, builder);
        return builder.ToString();
    }

    private static void WriteString(string value, StringBuilder builder)
    {
        var normalized = value.Normalize(NormalizationForm.FormC);
        builder.Append('"');
        foreach (var ch in normalized)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)ch:x4}");
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
