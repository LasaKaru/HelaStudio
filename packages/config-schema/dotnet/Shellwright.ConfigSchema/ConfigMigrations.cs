using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema;

/// <summary>One step from a schema version to the next.</summary>
/// <remarks>
/// Migrations operate on raw JSON, never on typed models: the typed model always
/// represents the current version, so a migration written against it silently
/// breaks the moment the schema moves again.
/// </remarks>
public interface IConfigMigration
{
    /// <summary>The version this migration reads.</summary>
    int FromVersion { get; }

    /// <summary>The version this migration writes.</summary>
    int ToVersion { get; }

    /// <summary>Migrates a document forward. Must be pure.</summary>
    /// <param name="config">The document to migrate.</param>
    /// <returns>A new document at <see cref="ToVersion"/>.</returns>
    JsonObject Up(JsonObject config);

    /// <summary>
    /// Migrates a document back, when the change is reversible.
    /// </summary>
    /// <param name="config">The document to migrate.</param>
    /// <returns>A new document at <see cref="FromVersion"/>, or null when lossy.</returns>
    JsonObject? Down(JsonObject config);
}

/// <summary>Raised when a document cannot be migrated to the current version.</summary>
public sealed class MigrationException : Exception
{
    /// <summary>Creates a migration failure with a stable diagnostic code.</summary>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">User-facing explanation.</param>
    public MigrationException(string code, string message)
        : base(message) => this.Code = code;

    /// <summary>Creates a migration failure with the default code.</summary>
    public MigrationException()
        : this(DiagnosticCode.SchemaVersionUnsupported, "The configuration could not be migrated.")
    {
    }

    /// <summary>Creates a migration failure with the default code and a message.</summary>
    /// <param name="message">User-facing explanation.</param>
    public MigrationException(string message)
        : this(DiagnosticCode.SchemaVersionUnsupported, message)
    {
    }

    /// <summary>Creates a migration failure wrapping an inner exception.</summary>
    /// <param name="message">User-facing explanation.</param>
    /// <param name="innerException">The underlying failure.</param>
    public MigrationException(string message, Exception innerException)
        : base(message, innerException) => this.Code = DiagnosticCode.SchemaVersionUnsupported;

    /// <summary>Stable diagnostic code, matching the validation code table.</summary>
    public string Code { get; } = DiagnosticCode.SchemaVersionUnsupported;
}

/// <summary>
/// v0 to v1.
/// </summary>
/// <remarks>
/// v0 was the pre-release shape used during Sprint 00 spikes. Two things changed:
/// <c>startUrl</c> became <c>app.initialUrl</c>, and link rules gained stable ids
/// so the studio can track them across a reorder.
/// </remarks>
public sealed class MigrationV0ToV1 : IConfigMigration
{
    /// <inheritdoc/>
    public int FromVersion => 0;

    /// <inheritdoc/>
    public int ToVersion => 1;

    /// <inheritdoc/>
    public JsonObject Up(JsonObject config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var next = (JsonObject)config.DeepClone();
        next["schemaVersion"] = 1;

        if (next["startUrl"] is JsonValue value && value.TryGetValue<string>(out var startUrl))
        {
            var app = next["app"] as JsonObject ?? [];
            app["initialUrl"] = startUrl;
            next["app"] = app;
            next.Remove("startUrl");
        }

        if (next["linkRules"] is JsonArray rules)
        {
            var migrated = new JsonArray();
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule is not JsonObject obj || obj["id"] is not null)
                {
                    migrated.Add(rule?.DeepClone());
                    continue;
                }

                var withId = (JsonObject)obj.DeepClone();
                withId["id"] = $"rule-{(i + 1).ToString(CultureInfo.InvariantCulture)}";
                migrated.Add(withId);
            }

            next["linkRules"] = migrated;
        }

        return next;
    }

    /// <inheritdoc/>
    public JsonObject? Down(JsonObject config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var next = (JsonObject)config.DeepClone();
        next["schemaVersion"] = 0;

        if (next["app"] is JsonObject app
            && app["initialUrl"] is JsonValue value
            && value.TryGetValue<string>(out var initialUrl))
        {
            next["startUrl"] = initialUrl;
            app.Remove("initialUrl");
            if (app.Count == 0)
            {
                next.Remove("app");
            }
        }

        if (next["linkRules"] is JsonArray rules)
        {
            var stripped = new JsonArray();
            foreach (var rule in rules)
            {
                if (rule is not JsonObject obj)
                {
                    stripped.Add(rule?.DeepClone());
                    continue;
                }

                var withoutId = (JsonObject)obj.DeepClone();
                withoutId.Remove("id");
                stripped.Add(withoutId);
            }

            next["linkRules"] = stripped;
        }

        return next;
    }
}

/// <summary>Walks a stored configuration up to the current schema version.</summary>
public static class ConfigMigrator
{
    /// <summary>Every migration this build knows, in ascending order.</summary>
    public static ImmutableArray<IConfigMigration> Migrations { get; } = [new MigrationV0ToV1()];

    /// <summary>
    /// Migrates a document up to the current schema version.
    /// </summary>
    /// <param name="config">The stored document.</param>
    /// <param name="available">The migrations to use. Defaults to <see cref="Migrations"/>.</param>
    /// <returns>The document at the current version.</returns>
    /// <exception cref="MigrationException">
    /// If the version is missing, from the future, or if no migration path exists.
    /// </exception>
    /// <remarks>
    /// A document already at the current version is returned unchanged, so
    /// migrating twice is safe and hash-stable.
    /// </remarks>
    public static JsonObject MigrateToCurrent(
        JsonObject config,
        IReadOnlyList<IConfigMigration>? available = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var steps = available ?? Migrations;

        if (config["schemaVersion"] is not JsonValue value
            || !value.TryGetValue<int>(out var version)
            || version < 0)
        {
            throw new MigrationException(
                DiagnosticCode.SchemaVersionUnsupported,
                "This configuration has no readable schemaVersion, so there is no way to tell which format it " +
                "is in. Add \"schemaVersion\": 1 if it was written against the current format.");
        }

        if (version > ConfigValidator.CurrentSchemaVersion)
        {
            throw new MigrationException(
                DiagnosticCode.SchemaVersionUnsupported,
                $"This configuration is at schema version {version.ToString(CultureInfo.InvariantCulture)}, " +
                $"which is newer than the version {ConfigValidator.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)} " +
                "this build understands. Update before opening it.");
        }

        var current = config;
        var at = version;
        while (at < ConfigValidator.CurrentSchemaVersion)
        {
            var step = steps.FirstOrDefault(m => m.FromVersion == at)
                ?? throw new MigrationException(
                    DiagnosticCode.SchemaVersionUnsupported,
                    $"No migration exists from schema version {at.ToString(CultureInfo.InvariantCulture)} " +
                    $"to {(at + 1).ToString(CultureInfo.InvariantCulture)}.");

            current = step.Up(current);
            at = step.ToVersion;
        }

        return current;
    }
}
