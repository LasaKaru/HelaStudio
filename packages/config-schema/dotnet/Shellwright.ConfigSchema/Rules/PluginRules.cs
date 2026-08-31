using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>One enabled plugin, resolved against the registry.</summary>
internal sealed record EnabledPlugin(string Id, JsonObject Config, PluginDescriptor? Descriptor);

/// <summary>Shared access to the enabled plugin list.</summary>
internal static class EnabledPlugins
{
    internal static List<EnabledPlugin> Of(RuleContext context) =>
    [
        .. Obj(context.Config["plugins"])
            .Select(pair => new EnabledPlugin(pair.Key, Obj(pair.Value), context.Plugins.Find(pair.Key))),
    ];
}

/// <summary>A plugin id that is not in the registry cannot be built.</summary>
public sealed class PluginKnownRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "plugin-known";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return EnabledPlugins.Of(context)
            .Where(p => p.Descriptor is null)
            .Select(p => Diagnostic.Create(
                DiagnosticCode.PluginUnknown,
                Severity.Error,
                JsonPointer.Of("plugins", p.Id),
                $"There is no plugin called \"{p.Id}\". Check the spelling against the plugin library, " +
                "or remove this entry."));
    }
}

/// <summary>Each plugin's settings must satisfy that plugin's own schema.</summary>
public sealed class PluginConfigRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "plugin-config";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var plugin in EnabledPlugins.Of(context))
        {
            if (plugin.Descriptor is not { } descriptor)
            {
                continue;
            }

            var schema = JsonSchema.FromText(descriptor.ConfigSchema.ToJsonString());
            var evaluation = schema.Evaluate(
                plugin.Config,
                new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });

            if (evaluation.IsValid)
            {
                continue;
            }

            foreach (var detail in Failures(evaluation))
            {
                yield return Diagnostic.Create(
                    DiagnosticCode.PluginConfigInvalid,
                    Severity.Error,
                    JsonPointer.Of("plugins", plugin.Id) + detail.Location,
                    $"{descriptor.Name}: this value {SchemaMessages.Constraint(detail.Keyword, detail.Message)}.");
            }
        }
    }

    private static IEnumerable<(string Location, string Keyword, string Message)> Failures(
        EvaluationResults results)
    {
        foreach (var node in Flatten(results))
        {
            if (node.Errors is null)
            {
                continue;
            }

            foreach (var (keyword, message) in node.Errors)
            {
                yield return (node.InstanceLocation.ToString(), keyword, message);
            }
        }
    }

    /// <summary>Walks the failing part of an evaluation tree; see ConfigValidator.Flatten.</summary>
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
}

/// <summary>Two plugins that declare a mutual conflict cannot ship in one app.</summary>
public sealed class PluginConflictRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "plugin-conflict";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var plugins = EnabledPlugins.Of(context);
        var ids = plugins.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var plugin in plugins)
        {
            if (plugin.Descriptor is not { } descriptor)
            {
                continue;
            }

            foreach (var otherId in descriptor.ConflictsWith)
            {
                // Report the pair once, on the alphabetically first id.
                if (!ids.Contains(otherId) || string.CompareOrdinal(plugin.Id, otherId) > 0)
                {
                    continue;
                }

                var reason = descriptor.ConflictReasons.TryGetValue(otherId, out var text)
                    ? text
                    : "They cannot be used together.";

                yield return Diagnostic.Create(
                    DiagnosticCode.PluginConflict,
                    Severity.Error,
                    JsonPointer.Of("plugins", plugin.Id),
                    $"\"{plugin.Id}\" and \"{otherId}\" conflict. {reason} Remove one of them.");
            }
        }
    }
}

/// <summary>A plugin cannot require a newer platform than the app targets.</summary>
public sealed class PluginPlatformFloorRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "plugin-platform-floor";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var build = Obj(context.Config["build"]);
        var minSdk = Int(Obj(build["android"])["minSdk"]) ?? 24;
        var minIos = Str(Obj(build["ios"])["minVersion"]) ?? "15.0";

        foreach (var plugin in EnabledPlugins.Of(context))
        {
            if (plugin.Descriptor is not { } descriptor)
            {
                continue;
            }

            if (descriptor.MinSdkAndroid > minSdk)
            {
                var needed = descriptor.MinSdkAndroid.ToString(CultureInfo.InvariantCulture);
                yield return Diagnostic.Create(
                    DiagnosticCode.PluginMinSdk,
                    Severity.Error,
                    JsonPointer.Of("plugins", plugin.Id),
                    $"{descriptor.Name} needs Android API {needed} or newer, but this app targets " +
                    $"API {minSdk.ToString(CultureInfo.InvariantCulture)}. " +
                    $"Raise build.android.minSdk to {needed}, or remove the plugin.");
            }

            if (CompareVersions(descriptor.MinVersionIos, minIos) > 0)
            {
                yield return Diagnostic.Create(
                    DiagnosticCode.PluginMinSdk,
                    Severity.Error,
                    JsonPointer.Of("plugins", plugin.Id),
                    $"{descriptor.Name} needs iOS {descriptor.MinVersionIos} or newer, but this app targets " +
                    $"iOS {minIos}. Raise build.ios.minVersion to {descriptor.MinVersionIos}, or remove the plugin.");
            }
        }
    }

    /// <summary>Compares dotted version strings numerically.</summary>
    private static int CompareVersions(string a, string b)
    {
        var left = Parse(a);
        var right = Parse(b);
        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var diff = At(left, i) - At(right, i);
            if (diff != 0)
            {
                return Math.Sign(diff);
            }
        }

        return 0;

        static List<int> Parse(string version) =>
        [
            .. version.Split('.').Select(part =>
                int.TryParse(part, CultureInfo.InvariantCulture, out var number) ? number : 0),
        ];

        static int At(List<int> parts, int index) => index < parts.Count ? parts[index] : 0;
    }
}

/// <summary>A plugin whose permission is switched off will fail at runtime.</summary>
public sealed class PluginPermissionRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "plugin-permission";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var permissions = Obj(context.Config["permissions"]);

        foreach (var plugin in EnabledPlugins.Of(context))
        {
            if (plugin.Descriptor is not { } descriptor)
            {
                continue;
            }

            foreach (var permission in descriptor.RequiredPermissions)
            {
                if (IsRequested(permissions[permission]))
                {
                    continue;
                }

                yield return Diagnostic.Create(
                    DiagnosticCode.PluginPermissionMissing,
                    Severity.Error,
                    JsonPointer.Of("permissions", permission),
                    $"{descriptor.Name} cannot work without the {permission} permission, which is currently off. " +
                    $"Turn on permissions.{permission}, or remove the plugin.");
            }
        }
    }
}
