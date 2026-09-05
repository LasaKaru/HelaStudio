using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema;

/// <summary>What a plugin declares about itself, as far as configuration validation cares.</summary>
/// <param name="Id">Stable plugin id, matching the manifest.</param>
/// <param name="Name">Human-readable name, used in diagnostic messages.</param>
/// <param name="MinSdkAndroid">Minimum Android API level the plugin supports.</param>
/// <param name="MinVersionIos">Minimum iOS version the plugin supports.</param>
/// <param name="RequiredPermissions">Device permissions the plugin cannot work without.</param>
/// <param name="ConflictsWith">Ids of plugins that cannot be enabled alongside this one.</param>
/// <param name="ConflictReasons">Why those conflicts exist, keyed by conflicting plugin id.</param>
/// <param name="ConfigSchema">JSON Schema for this plugin's entry in <c>plugins</c>.</param>
public sealed record PluginDescriptor(
    string Id,
    string Name,
    int MinSdkAndroid,
    string MinVersionIos,
    ImmutableArray<string> RequiredPermissions,
    ImmutableArray<string> ConflictsWith,
    ImmutableDictionary<string, string> ConflictReasons,
    JsonObject ConfigSchema);

/// <summary>A source of plugin descriptors.</summary>
public interface IPluginRegistry
{
    /// <summary>Returns the descriptor for an id, or null if no such plugin exists.</summary>
    /// <param name="id">The plugin id.</param>
    /// <returns>The descriptor, or null.</returns>
    PluginDescriptor? Find(string id);
}

/// <summary>
/// Plugins known at Sprint 01.
/// </summary>
/// <remarks>
/// Deliberately small, and identical to <c>src/plugin-registry.ts</c>. Each entry
/// is replaced by its real manifest in S10; the shape here exists so the plugin
/// rules have something to validate against.
/// </remarks>
public sealed class BuiltInPluginRegistry : IPluginRegistry
{
    /// <summary>The shared instance.</summary>
    public static readonly BuiltInPluginRegistry Instance = new();

    private static readonly ImmutableDictionary<string, PluginDescriptor> ById = Build();

    /// <summary>Every built-in plugin.</summary>
    public static IReadOnlyCollection<PluginDescriptor> All => ById.Values.ToList();

    /// <inheritdoc/>
    public PluginDescriptor? Find(string id) => ById.TryGetValue(id, out var found) ? found : null;

    private static ImmutableDictionary<string, PluginDescriptor> Build()
    {
        var plugins = new[]
        {
            Descriptor("haptics", "Haptic Feedback", 24, "15.0", [], [], EmptySchema()),
            Descriptor(
                "biometric",
                "Face ID and Fingerprint",
                24,
                "15.0",
                ["biometric"],
                [],
                Schema(new JsonObject
                {
                    ["promptReason"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["minLength"] = 1,
                        ["maxLength"] = 120,
                    },
                })),
            Descriptor(
                "qr-scanner",
                "QR and Barcode Scanner",
                24,
                "15.0",
                ["camera"],
                ["scandit-scanner"],
                Schema(new JsonObject
                {
                    ["formats"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["enum"] = new JsonArray("qr", "ean13", "ean8", "code128", "code39", "pdf417", "dataMatrix"),
                        },
                    },
                    ["beepOnScan"] = new JsonObject { ["type"] = "boolean" },
                    ["torchButton"] = new JsonObject { ["type"] = "boolean" },
                }),
                new Dictionary<string, string>
                {
                    ["scandit-scanner"] = "Both register a camera scanning surface.",
                }),
            Descriptor(
                "scandit-scanner",
                "Scandit Enterprise Scanning",
                26,
                "16.0",
                ["camera"],
                ["qr-scanner"],
                Schema(new JsonObject { ["licenceKeyRef"] = new JsonObject { ["type"] = "string" } }),
                new Dictionary<string, string>
                {
                    ["qr-scanner"] = "Both register a camera scanning surface.",
                }),
            Descriptor(
                "push",
                "Push Notifications",
                24,
                "15.0",
                ["notifications"],
                [],
                Schema(new JsonObject
                {
                    ["provider"] = new JsonObject { ["enum"] = new JsonArray("shellwright", "onesignal", "fcm") },
                    ["promptOnLaunch"] = new JsonObject { ["type"] = "boolean" },
                })),
            Descriptor(
                "iap",
                "In-App Purchases",
                24,
                "15.0",
                [],
                [],
                Schema(new JsonObject
                {
                    ["productsUrl"] = new JsonObject { ["type"] = "string", ["pattern"] = "^https://" },
                })),
            Descriptor(
                "document-scanner",
                "Document Scanner",
                26,
                "16.0",
                ["camera"],
                [],
                Schema(new JsonObject
                {
                    ["outputFormat"] = new JsonObject { ["enum"] = new JsonArray("pdf", "jpeg") },
                })),
            Descriptor(
                "nfc",
                "NFC Tag Scanner",
                26,
                "15.0",
                [],
                [],
                Schema(new JsonObject { ["readOnly"] = new JsonObject { ["type"] = "boolean" } })),
        };

        return plugins.ToImmutableDictionary(p => p.Id, StringComparer.Ordinal);
    }

    private static PluginDescriptor Descriptor(
        string id,
        string name,
        int minSdk,
        string minIos,
        string[] permissions,
        string[] conflicts,
        JsonObject configSchema,
        Dictionary<string, string>? reasons = null) =>
        new(
            id,
            name,
            minSdk,
            minIos,
            [.. permissions],
            [.. conflicts],
            (reasons ?? []).ToImmutableDictionary(StringComparer.Ordinal),
            configSchema);

    private static JsonObject EmptySchema() => Schema([]);

    private static JsonObject Schema(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["additionalProperties"] = false,
    };
}
