using System.Collections.Immutable;

namespace Shellwright.ConfigSchema;

/// <summary>How much a diagnostic matters.</summary>
public enum Severity
{
    /// <summary>Blocks a save and a build.</summary>
    Error,

    /// <summary>Allowed through, but surfaced prominently.</summary>
    Warning,

    /// <summary>A hint or suggestion.</summary>
    Info,
}

/// <summary>
/// The stable diagnostic codes.
/// </summary>
/// <remarks>
/// Codes are permanent and must match <c>src/diagnostics.ts</c> exactly. If a
/// rule is removed its code is retired, never reused — customers and support
/// articles reference these strings.
/// </remarks>
public static class DiagnosticCode
{
    /// <summary>The document does not match the schema.</summary>
    public const string SchemaViolation = "CFG_SCHEMA_VIOLATION";

    /// <summary>The document is from a schema version this build cannot read.</summary>
    public const string SchemaVersionUnsupported = "CFG_SCHEMA_VERSION_UNSUPPORTED";

    /// <summary>A field name is not recognised.</summary>
    public const string UnknownField = "CFG_UNKNOWN_FIELD";

    /// <summary>The bundle identifier is not valid reverse-DNS.</summary>
    public const string BundleIdInvalid = "CFG_BUNDLE_ID_INVALID";

    /// <summary>The app name exceeds the App Store limit.</summary>
    public const string NameTooLong = "CFG_NAME_TOO_LONG";

    /// <summary>The start URL is not under an allowed origin.</summary>
    public const string InitialUrlNotAllowed = "CFG_INITIAL_URL_NOT_ALLOWED";

    /// <summary>An internal destination is not under an allowed origin.</summary>
    public const string OriginNotCovered = "CFG_ORIGIN_NOT_COVERED";

    /// <summary>A plain-http URL appears in the document.</summary>
    public const string CleartextUrl = "CFG_CLEARTEXT_URL";

    /// <summary>A user pattern does not compile.</summary>
    public const string RegexInvalid = "CFG_REGEX_INVALID";

    /// <summary>A user pattern can backtrack catastrophically.</summary>
    public const string RegexCatastrophic = "CFG_REGEX_CATASTROPHIC";

    /// <summary>A link rule is shadowed by an earlier, broader rule.</summary>
    public const string LinkRuleUnreachable = "CFG_LINK_RULE_UNREACHABLE";

    /// <summary>No terminal catch-all rule exists.</summary>
    public const string LinkRuleNoCatchall = "CFG_LINK_RULE_NO_CATCHALL";

    /// <summary>More tabs than iOS displays comfortably.</summary>
    public const string TabCountHigh = "CFG_TAB_COUNT_HIGH";

    /// <summary>Two items in one list share an identifier.</summary>
    public const string DuplicateItemId = "CFG_DUPLICATE_ITEM_ID";

    /// <summary>The app has no native surface and risks a guideline 4.2 rejection.</summary>
    public const string NoNativeFeatures = "CFG_NO_NATIVE_FEATURES";

    /// <summary>A permission is requested that nothing uses.</summary>
    public const string PermissionUnjustified = "CFG_PERMISSION_UNJUSTIFIED";

    /// <summary>A plugin id is not in the registry.</summary>
    public const string PluginUnknown = "CFG_PLUGIN_UNKNOWN";

    /// <summary>A plugin's settings fail its own schema.</summary>
    public const string PluginConfigInvalid = "CFG_PLUGIN_CONFIG_INVALID";

    /// <summary>Two enabled plugins declare a mutual conflict.</summary>
    public const string PluginConflict = "CFG_PLUGIN_CONFLICT";

    /// <summary>A plugin requires a newer platform than the app targets.</summary>
    public const string PluginMinSdk = "CFG_PLUGIN_MIN_SDK";

    /// <summary>A plugin's required permission is switched off.</summary>
    public const string PluginPermissionMissing = "CFG_PLUGIN_PERMISSION_MISSING";

    /// <summary>A referenced asset is not in storage.</summary>
    public const string AssetMissing = "CFG_ASSET_MISSING";

    /// <summary>The source icon is too small or not square.</summary>
    public const string IconDimensions = "CFG_ICON_DIMENSIONS";

    /// <summary>The source icon carries an alpha channel.</summary>
    public const string IconAlpha = "CFG_ICON_ALPHA";

    /// <summary>A credential appears in the configuration.</summary>
    public const string SecretInConfig = "CFG_SECRET_IN_CONFIG";

    /// <summary>A string carries an unprintable control character.</summary>
    public const string ControlCharacter = "CFG_CONTROL_CHARACTER";
}

/// <summary>A single finding about a configuration document.</summary>
/// <param name="Code">Stable, documented, searchable code.</param>
/// <param name="Severity">Whether this blocks a save and build.</param>
/// <param name="Path">RFC 6901 JSON Pointer to the offending value.</param>
/// <param name="Message">User-facing text that names the fix.</param>
/// <param name="DocsUrl">Where to read more.</param>
public sealed record Diagnostic(
    string Code,
    Severity Severity,
    string Path,
    string Message,
    string DocsUrl)
{
    private const string DocsBase = "https://docs.shellwright.dev/reference/diagnostics";

    /// <summary>Builds a diagnostic, deriving its documentation URL from the code.</summary>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="severity">How much the finding matters.</param>
    /// <param name="path">JSON Pointer to the offending value.</param>
    /// <param name="message">User-facing, actionable text.</param>
    /// <returns>The diagnostic.</returns>
    public static Diagnostic Create(string code, Severity severity, string path, string message)
    {
        ArgumentNullException.ThrowIfNull(code);

        // CA1308 warns against lowercasing because it is unsafe for normalising
        // text before a security comparison. This is neither: it builds a URL
        // fragment from a fixed ASCII constant, and the documentation anchors are
        // lowercase. The TypeScript side does the same, and the contract test
        // asserts the two produce identical URLs.
#pragma warning disable CA1308
        var anchor = code.ToLowerInvariant();
#pragma warning restore CA1308

        return new Diagnostic(code, severity, path, message, $"{DocsBase}#{anchor}");
    }
}

/// <summary>The outcome of validating a configuration document.</summary>
/// <param name="Valid">True when there are no errors. Warnings do not block.</param>
/// <param name="Errors">Findings that block a save and a build.</param>
/// <param name="Warnings">Findings allowed through but surfaced prominently.</param>
/// <param name="Info">Hints and suggestions.</param>
public sealed record ValidationResult(
    bool Valid,
    ImmutableArray<Diagnostic> Errors,
    ImmutableArray<Diagnostic> Warnings,
    ImmutableArray<Diagnostic> Info)
{
    /// <summary>
    /// Groups diagnostics into a result, sorting each bucket by path then code.
    /// </summary>
    /// <remarks>
    /// Rules may run in any order, so ordering is imposed here. Non-deterministic
    /// error order breaks snapshot tests and makes the studio's error list jump
    /// around while the user types.
    /// </remarks>
    /// <param name="diagnostics">The findings to group.</param>
    /// <returns>The grouped, sorted result.</returns>
    public static ValidationResult From(IEnumerable<Diagnostic> diagnostics)
    {
        var sorted = diagnostics
            .OrderBy(d => d.Path, StringComparer.Ordinal)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ToList();

        var errors = sorted.Where(d => d.Severity == Severity.Error).ToImmutableArray();
        return new ValidationResult(
            errors.Length == 0,
            errors,
            sorted.Where(d => d.Severity == Severity.Warning).ToImmutableArray(),
            sorted.Where(d => d.Severity == Severity.Info).ToImmutableArray());
    }
}

/// <summary>Helpers for building RFC 6901 JSON Pointers.</summary>
public static class JsonPointer
{
    /// <summary>Escapes one path segment per RFC 6901.</summary>
    /// <param name="segment">The raw segment.</param>
    /// <returns>The escaped segment.</returns>
    public static string EscapeSegment(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        return segment.Replace("~", "~0", StringComparison.Ordinal)
                      .Replace("/", "~1", StringComparison.Ordinal);
    }

    /// <summary>Joins segments into a JSON Pointer.</summary>
    /// <param name="segments">The path segments, strings or integers.</param>
    /// <returns>The pointer, or an empty string for the document root.</returns>
    public static string Of(params object[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments.Length == 0
            ? string.Empty
            : "/" + string.Join('/', segments.Select(s =>
                s is string text ? EscapeSegment(text) : Convert.ToString(s, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
