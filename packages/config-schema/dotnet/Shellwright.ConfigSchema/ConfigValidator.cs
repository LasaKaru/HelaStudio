using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Json.Schema;
using Shellwright.ConfigSchema.Rules;

namespace Shellwright.ConfigSchema;

/// <summary>A validated configuration, with defaults resolved.</summary>
/// <param name="Result">Diagnostics grouped by severity.</param>
/// <param name="Resolved">The document with every schema default filled in.</param>
public sealed record ValidatedConfig(ValidationResult Result, JsonObject Resolved);

/// <summary>
/// The validation entry point.
/// </summary>
/// <remarks>
/// Validation runs three times, cheapest machine first: in the browser on every
/// keystroke, at the API on save, and on the runner before a build. A config
/// error must never reach a macOS runner — that single rule is worth more than
/// every other optimisation in the system.
///
/// This must produce output identical to <c>src/validate.ts</c>. The cross-language
/// contract test asserts it against the shared fixture corpus.
/// </remarks>
public sealed class ConfigValidator
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly Lazy<JsonObject> SchemaDocument = new(LoadSchemaDocument);
    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(SchemaDocument.Value.ToJsonString()));

    private readonly ImmutableArray<IValidationRule> rules;
    private readonly IPluginRegistry plugins;
    private readonly IAssetResolver? assets;

    /// <summary>Creates a validator.</summary>
    /// <param name="plugins">Plugin registry. Defaults to the built-in set.</param>
    /// <param name="assets">Asset store, when available. Asset rules skip without one.</param>
    /// <param name="rules">Rule set to run. Defaults to every rule.</param>
    public ConfigValidator(
        IPluginRegistry? plugins = null,
        IAssetResolver? assets = null,
        IEnumerable<IValidationRule>? rules = null)
    {
        this.plugins = plugins ?? BuiltInPluginRegistry.Instance;
        this.assets = assets;
        this.rules = rules?.ToImmutableArray() ?? DefaultRules;
    }

    /// <summary>
    /// The default rule set.
    /// </summary>
    /// <remarks>
    /// Order here does not affect output — results are sorted by path and code in
    /// <see cref="ValidationResult.From"/> so diagnostics are deterministic.
    /// </remarks>
    public static ImmutableArray<IValidationRule> DefaultRules { get; } =
    [
        new InitialUrlAllowedRule(),
        new OriginCoverageRule(),
        new CleartextUrlRule(),
        new RegexSafetyRule(),
        new UnreachableLinkRuleRule(),
        new CatchAllLinkRuleRule(),
        new TabCountRule(),
        new NativeFeaturesRule(),
        new PermissionJustifiedRule(),
        new DuplicateItemIdRule(),
        new PluginKnownRule(),
        new PluginConfigRule(),
        new PluginConflictRule(),
        new PluginPlatformFloorRule(),
        new PluginPermissionRule(),
        new AssetExistsRule(),
        new IconDimensionsRule(),
        new IconAlphaRule(),
        new NoSecretsRule(),
        new NoControlCharactersRule(),
    ];

    /// <summary>The <c>appconfig.json</c> v1 JSON Schema, as a mutable document.</summary>
    public static JsonObject SchemaJson => SchemaDocument.Value;

    /// <summary>Validates a configuration document and resolves its defaults.</summary>
    /// <param name="config">The document to validate.</param>
    /// <returns>The diagnostics and the resolved document.</returns>
    /// <remarks>
    /// Schema violations short-circuit the semantic rules: running rules against a
    /// document of the wrong shape produces a cascade of confusing secondary errors.
    /// </remarks>
    public ValidatedConfig Validate(JsonNode? config)
    {
        var asObject = config as JsonObject ?? [];

        if (CheckSchemaVersion(asObject) is { } versionError)
        {
            return new ValidatedConfig(ValidationResult.From([versionError]), asObject);
        }

        // Hierarchical, not List: List flattens every node under the root, which
        // makes it impossible to tell a genuine failure from the `oneOf` branch
        // that simply did not match. See SchemaDiagnostics.
        var evaluation = Schema.Value.Evaluate(
            config,
            new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });

        if (!evaluation.IsValid)
        {
            return new ValidatedConfig(
                ValidationResult.From(SchemaDiagnostics(evaluation)),
                asObject);
        }

        var resolved = SchemaDefaults.Resolve(config, SchemaDocument.Value) as JsonObject ?? [];
        var context = new RuleContext(resolved, this.plugins, this.assets);
        var diagnostics = this.rules.SelectMany(rule => rule.Run(context));

        return new ValidatedConfig(ValidationResult.From(diagnostics), resolved);
    }

    /// <summary>Rejects a document written against a schema version this build cannot read.</summary>
    private static Diagnostic? CheckSchemaVersion(JsonObject config)
    {
        if (config["schemaVersion"] is not JsonValue value
            || !value.TryGetValue<int>(out var version)
            || version <= CurrentSchemaVersion)
        {
            return null;
        }

        return Diagnostic.Create(
            DiagnosticCode.SchemaVersionUnsupported,
            Severity.Error,
            "/schemaVersion",
            $"This configuration was written for schema version {version.ToString(CultureInfo.InvariantCulture)}, " +
            $"but this build understands up to version {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}. " +
            "Update to a newer release before opening it.");
    }

    private static IEnumerable<Diagnostic> SchemaDiagnostics(EvaluationResults results)
    {
        var seen = new HashSet<(string, string, string)>();

        foreach (var node in Flatten(results))
        {
            if (node.Errors is null)
            {
                continue;
            }

            var path = node.InstanceLocation.ToString();

            foreach (var error in node.Errors)
            {
                var errorKey = error.Key;
                var message = error.Value;

                // Some failures carry an empty error key — `unevaluatedProperties`
                // reports only "All values fail against the false schema" — so the
                // keyword is recovered from the evaluation path instead.
                var keyword = errorKey.Length > 0 ? errorKey : LastSegment(node.EvaluationPath.ToString());
                var code = SpecificCode(keyword, path);
                var text = SchemaMessages.Describe(keyword, path, message, LastSegment(path));

                // The same failure can surface under several schema keywords;
                // report each distinct finding once.
                if (seen.Add((code, path, text)))
                {
                    yield return Diagnostic.Create(code, Severity.Error, path, text);
                }
            }
        }
    }

    /// <summary>
    /// Walks the failing part of an evaluation tree.
    /// </summary>
    /// <remarks>
    /// A node that passed is not descended into. This matters for <c>oneOf</c>:
    /// the branch that did not match is recorded as failing even when the
    /// <c>oneOf</c> as a whole succeeded, and reporting it would tell a user that
    /// a perfectly valid tab label "should be an object".
    /// </remarks>
    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults results)
    {
        if (results.IsValid)
        {
            yield break;
        }

        yield return results;
        foreach (var child in results.Details.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    /// <summary>The final segment of a JSON Pointer, unescaped.</summary>
    private static string LastSegment(string pointer)
    {
        var index = pointer.LastIndexOf('/');
        var segment = index < 0 ? pointer : pointer[(index + 1)..];
        return segment
            .Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }

    private static string SpecificCode(string keyword, string path) => path switch
    {
        "/app/bundleId" => DiagnosticCode.BundleIdInvalid,
        "/app/name" when keyword == "maxLength" => DiagnosticCode.NameTooLong,
        _ when keyword is "unevaluatedProperties" or "additionalProperties" => DiagnosticCode.UnknownField,
        _ => DiagnosticCode.SchemaViolation,
    };

    private static JsonObject LoadSchemaDocument()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("appconfig.v1.json")
            ?? throw new InvalidOperationException("The appconfig schema is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject
            ?? throw new InvalidOperationException("The appconfig schema is not a JSON object.");
    }
}
