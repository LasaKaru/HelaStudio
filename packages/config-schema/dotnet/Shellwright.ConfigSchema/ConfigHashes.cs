using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema;

/// <summary>Inputs to hashing that come from outside the config document.</summary>
/// <param name="ShellVersion">Semver of the shell template the app is built from.</param>
/// <param name="PluginLock">Exact resolved plugin versions, id to version.</param>
/// <param name="Toolchain">Toolchain identity, such as Xcode and AGP versions.</param>
public sealed record HashContext(
    string ShellVersion,
    IReadOnlyDictionary<string, string>? PluginLock = null,
    IReadOnlyDictionary<string, string>? Toolchain = null);

/// <summary>The three cache keys derived from a resolved configuration.</summary>
/// <param name="CodeKey">Changes here force a full native recompile.</param>
/// <param name="AssetKey">Changes here need only a resource repackage.</param>
/// <param name="ContentKey">Changes here need only a config patch and a re-sign.</param>
public sealed record ConfigHashes(string CodeKey, string AssetKey, string ContentKey);

/// <summary>
/// Computes the three-way build cache key.
/// </summary>
/// <remarks>
/// A single hash over the whole config would mean every change forces a full
/// recompile. Splitting the key by what a change actually costs is the highest
/// leverage optimisation in the system: roughly 70-80% of user-triggered builds
/// touch only assets or content, and those take seconds rather than minutes.
///
/// The projections must match <c>src/hash.ts</c> exactly.
/// </remarks>
public static class ConfigHasher
{
    /// <summary>Keys that carry user-visible text or imagery, and so belong to the asset key.</summary>
    private static readonly HashSet<string> LabelKeys = ["label", "icon", "staticTitle", "section"];

    /// <summary>Hashes canonical bytes with BLAKE3, returning lowercase hex.</summary>
    /// <param name="node">The value to hash.</param>
    /// <returns>A 64-character lowercase hex digest.</returns>
    public static string HashValue(JsonNode? node)
    {
        var bytes = CanonicalJson.SerializeToUtf8(node);
        return Convert.ToHexStringLower(global::Blake3.Hasher.Hash(bytes).AsSpan());
    }

    /// <summary>Computes all three cache keys for a resolved configuration.</summary>
    /// <param name="resolved">A configuration with schema defaults already resolved.</param>
    /// <param name="context">Inputs from outside the document.</param>
    /// <returns>The three cache keys.</returns>
    public static ConfigHashes Compute(JsonObject resolved, HashContext context)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(context);

        return new ConfigHashes(
            HashValue(ProjectCode(resolved, context)),
            HashValue(ProjectAsset(resolved)),
            HashValue(ProjectContent(resolved)));
    }

    private static JsonObject ProjectCode(JsonObject config, HashContext context)
    {
        var app = Obj(config["app"]);
        var surfaceTypes = new JsonArray();
        foreach (var surface in Arr(config["nativeSurfaces"]))
        {
            surfaceTypes.Add(Obj(surface)["type"]?.DeepClone());
        }

        return new JsonObject
        {
            ["bundleId"] = app["bundleId"]?.DeepClone(),
            ["permissions"] = config["permissions"]?.DeepClone(),
            ["plugins"] = config["plugins"]?.DeepClone(),
            ["nativeSurfaceTypes"] = surfaceTypes,
            ["deepLinks"] = config["deepLinks"]?.DeepClone(),
            ["build"] = config["build"]?.DeepClone(),
            ["shellVersion"] = context.ShellVersion,
            ["pluginLock"] = ToNode(context.PluginLock),
            ["toolchain"] = ToNode(context.Toolchain),
        };
    }

    private static JsonObject ProjectAsset(JsonObject config)
    {
        var app = Obj(config["app"]);
        return new JsonObject
        {
            ["name"] = app["name"]?.DeepClone(),
            ["branding"] = config["branding"]?.DeepClone(),
            ["labels"] = CollectLabels(config),
        };
    }

    private static JsonObject ProjectContent(JsonObject config)
    {
        var app = Obj(config["app"]);
        var surfaceConfig = new JsonArray();
        foreach (var surface in Arr(config["nativeSurfaces"]))
        {
            var item = Obj(surface);
            surfaceConfig.Add(new JsonObject
            {
                ["id"] = item["id"]?.DeepClone(),
                ["config"] = item["config"]?.DeepClone(),
            });
        }

        return new JsonObject
        {
            ["versionName"] = app["versionName"]?.DeepClone(),
            ["versionCode"] = app["versionCode"]?.DeepClone(),
            ["initialUrl"] = app["initialUrl"]?.DeepClone(),
            ["allowedOrigins"] = app["allowedOrigins"]?.DeepClone(),
            ["navigation"] = StripLabels(config["navigation"]),
            ["linkRules"] = config["linkRules"]?.DeepClone(),
            ["webOverrides"] = config["webOverrides"]?.DeepClone(),
            ["offline"] = config["offline"]?.DeepClone(),
            ["ota"] = config["ota"]?.DeepClone(),
            ["nativeSurfaceConfig"] = surfaceConfig,
        };
    }

    /// <summary>Collects every label and icon in navigation, in document order.</summary>
    private static JsonArray CollectLabels(JsonObject config)
    {
        var found = new JsonArray();
        Walk(config["navigation"], (key, value) =>
        {
            if (LabelKeys.Contains(key))
            {
                found.Add(value?.DeepClone());
            }
        });
        return found;
    }

    /// <summary>Returns navigation with label and icon fields removed, leaving only structure.</summary>
    private static JsonNode? StripLabels(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray array:
                {
                    var result = new JsonArray();
                    foreach (var item in array)
                    {
                        result.Add(StripLabels(item));
                    }

                    return result;
                }

            case JsonObject obj:
                {
                    var result = new JsonObject();
                    foreach (var (key, value) in obj)
                    {
                        if (!LabelKeys.Contains(key))
                        {
                            result[key] = StripLabels(value);
                        }
                    }

                    return result;
                }

            default:
                return node?.DeepClone();
        }
    }

    private static void Walk(JsonNode? node, Action<string, JsonNode?> visit)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array)
                {
                    Walk(item, visit);
                }

                return;

            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    visit(key, value);
                    Walk(value, visit);
                }

                return;

            default:
                return;
        }
    }

    private static JsonObject? ToNode(IReadOnlyDictionary<string, string>? map)
    {
        if (map is null)
        {
            return null;
        }

        var result = new JsonObject();
        foreach (var (key, value) in map)
        {
            result[key] = value;
        }

        return result;
    }

    private static JsonObject Obj(JsonNode? node) => node as JsonObject ?? [];

    private static JsonArray Arr(JsonNode? node) => node as JsonArray ?? [];
}
