using System.Text.RegularExpressions;

namespace Shellwright.ConfigSchema;

/// <summary>
/// Turns a schema failure into text a non-engineer can act on.
/// </summary>
/// <remarks>
/// A raw validator message describes the schema ("must match pattern ^[a-z]..."),
/// which tells the user nothing about what to type instead. These strings are
/// user-facing copy and must match <c>src/validate.ts</c> word for word.
/// </remarks>
internal static partial class SchemaMessages
{
    /// <summary>Describes a schema failure at a given instance path.</summary>
    /// <param name="keyword">The schema keyword that failed.</param>
    /// <param name="path">JSON Pointer to the offending value.</param>
    /// <param name="raw">The validator's own message, used only to recover parameters.</param>
    /// <param name="instanceKey">The last segment of the instance path, naming the offending field.</param>
    /// <returns>User-facing, actionable text.</returns>
    internal static string Describe(string keyword, string path, string raw, string instanceKey)
    {
        if (path == "/app/bundleId")
        {
            return "The bundle identifier must be lowercase reverse-DNS with at least one dot, such as " +
                "com.acme.app. Uppercase letters, spaces, and leading digits are rejected by both stores.";
        }

        if (path == "/app/name" && keyword == "maxLength")
        {
            return "The app name is longer than 30 characters, which is the App Store limit. Shorten it, or " +
                "use a shorter name on the icon and the full name in your store listing.";
        }

        if (keyword is "unevaluatedProperties" or "additionalProperties")
        {
            // The offending key is the last segment of the instance path. Ajv
            // reports it as a parameter; JsonSchema.Net reports only that the
            // value failed a false schema, so the path is the reliable source.
            var extra = instanceKey.Length > 0 ? instanceKey : "A field";
            return $"\"{extra}\" is not a recognised setting here. Check the spelling, or move it " +
                "under an \"x-\" prefixed object if it is your own data.";
        }

        if (keyword == "required")
        {
            var missing = PropertyName().Match(raw) is { Success: true } match
                ? match.Groups[1].Value
                : "A required field";
            return $"\"{missing}\" is required and is missing.";
        }

        if (keyword is "pattern" or "format")
        {
            return "This value is not in the expected format.";
        }

        return $"This value {Constraint(keyword, raw)}.";
    }

    /// <summary>
    /// Describes a failed constraint in our own words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately does not pass the validator's own message through. Ajv and
    /// JsonSchema.Net word the same failure differently — "must be boolean" versus
    /// "Value is &quot;string&quot; but should be &quot;boolean&quot;" — so passing
    /// either through would break the cross-language contract, and neither is
    /// written in a voice a non-engineer should have to read.
    /// </para>
    /// <para>
    /// The expected type is recovered from the validator's message because
    /// JsonSchema.Net does not expose the failing keyword's value. That extraction
    /// is the fragile half of this file; the contract test is what guards it, and
    /// an unmapped keyword falls back to wording both engines share rather than
    /// leaking either engine's phrasing.
    /// </para>
    /// </remarks>
    /// <param name="keyword">The schema keyword that failed.</param>
    /// <param name="raw">The validator's own message, used only to recover parameters.</param>
    /// <returns>A phrase completing the sentence "This value ...".</returns>
    internal static string Constraint(string keyword, string raw)
    {
        if (keyword != "type")
        {
            return keyword switch
            {
                "enum" or "const" => "must be one of the allowed values",
                "minLength" => "is too short",
                "maxLength" => "is too long",
                "minimum" or "exclusiveMinimum" => "is below the smallest allowed number",
                "maximum" or "exclusiveMaximum" => "is above the largest allowed number",
                "minItems" => "does not have enough entries",
                "maxItems" => "has too many entries",
                "uniqueItems" => "contains the same entry twice",
                "multipleOf" => "is not one of the allowed steps",
                _ => "is not valid here",
            };
        }

        var expected = ExpectedType().Match(raw) is { Success: true } match
            ? match.Groups[1].Value
            : string.Empty;

        return $"must be {TypeName(expected)}";
    }

    /// <summary>Names a JSON type the way a non-engineer would.</summary>
    /// <param name="jsonType">The JSON type name.</param>
    /// <returns>A readable noun phrase.</returns>
    internal static string TypeName(string jsonType) => jsonType switch
    {
        "boolean" => "either on or off",
        "string" => "text",
        "number" => "a number",
        "integer" => "a whole number",
        "array" => "a list",
        "object" => "a group of settings",
        "null" => "empty",
        _ => "a valid value",
    };

    /// <summary>Extracts a quoted property name from a validator message.</summary>
    [GeneratedRegex("[\"'`]([^\"'`]+)[\"'`]")]
    private static partial Regex PropertyName();

    /// <summary>Recovers the expected type from a JsonSchema.Net type message.</summary>
    [GeneratedRegex("should be \"([a-z]+)\"")]
    private static partial Regex ExpectedType();
}
