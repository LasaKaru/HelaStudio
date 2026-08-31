using System.Globalization;
using System.Text.Json.Nodes;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>iOS collapses tabs beyond the fifth into a "More" tab, which reads badly.</summary>
public sealed class TabCountRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "tab-count";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var items = Arr(Obj(Obj(context.Config["navigation"])["tabBar"])["items"]);
        if (items.Count <= 5)
        {
            yield break;
        }

        yield return Diagnostic.Create(
            DiagnosticCode.TabCountHigh,
            Severity.Warning,
            JsonPointer.Of("navigation", "tabBar", "items"),
            $"You have {items.Count.ToString(CultureInfo.InvariantCulture)} tabs. iOS shows only the first four " +
            "and hides the rest behind a \"More\" tab, which most users never open. " +
            "Keep five or fewer, and move the rest into a drawer.");
    }
}

/// <summary>An app with no native surface at all is very likely to be rejected.</summary>
public sealed class NativeFeaturesRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "native-features";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = context.Config;
        var navigation = Obj(config["navigation"]);

        var hasNative =
            EnabledWithItems(navigation["tabBar"])
            || EnabledWithItems(navigation["drawer"])
            || Obj(config["plugins"]).Count > 0
            || Arr(config["nativeSurfaces"]).Count > 0
            || Arr(Obj(config["deepLinks"])["universalLinks"]).Count > 0;

        if (hasNative)
        {
            yield break;
        }

        yield return Diagnostic.Create(
            DiagnosticCode.NoNativeFeatures,
            Severity.Warning,
            string.Empty,
            "This app has no native navigation, no plugins, and no native screens, so it is a web page in a " +
            "frame. Apple rejects these under App Store guideline 4.2. Add a tab bar or drawer, enable a " +
            "capability such as push notifications or biometric unlock, or add an onboarding screen.");
    }

    private static bool EnabledWithItems(JsonNode? node)
    {
        var value = Obj(node);
        return Bool(value["enabled"]) == true && Arr(value["items"]).Count > 0;
    }
}

/// <summary>Permissions with no plugin behind them.</summary>
/// <remarks>
/// Camera, microphone, and photo library are also reachable straight from a web
/// form, so those stay a warning rather than an error — but an unexplained
/// permission prompt is still one of the most common rejection reasons.
/// </remarks>
public sealed class PermissionJustifiedRule : IValidationRule
{
    private static readonly Dictionary<string, string[]> Justifications = new(StringComparer.Ordinal)
    {
        ["camera"] = ["qr-scanner", "scandit-scanner", "document-scanner"],
        ["microphone"] = [],
        ["photoLibrary"] = [],
        ["notifications"] = ["push"],
        ["contacts"] = [],
        ["calendar"] = [],
        ["biometric"] = ["biometric"],
    };

    /// <inheritdoc/>
    public string Name => "permission-justified";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var enabled = Obj(context.Config["plugins"]).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, value) in Obj(context.Config["permissions"]))
        {
            if (!IsRequested(value) || !Justifications.TryGetValue(name, out var justifiers))
            {
                continue;
            }

            if (justifiers.Any(enabled.Contains))
            {
                continue;
            }

            var advice = justifiers.Length > 0
                ? $"Enable the {string.Join(" or ", justifiers)} plugin, or turn this permission off."
                : "Turn it off unless your website genuinely asks for it.";

            yield return Diagnostic.Create(
                DiagnosticCode.PermissionUnjustified,
                Severity.Warning,
                JsonPointer.Of("permissions", name),
                $"Nothing in this configuration uses the {name} permission. Both stores ask why a permission is " +
                $"requested, and an unexplained prompt is a common rejection reason. {advice}");
        }
    }
}

/// <summary>Ids in a list must be unique, or the studio cannot track items across edits.</summary>
public sealed class DuplicateItemIdRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "duplicate-id";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var config = context.Config;
        var navigation = Obj(config["navigation"]);

        (object[] Path, JsonNode? List)[] lists =
        [
            ([ "navigation", "tabBar", "items" ], Obj(navigation["tabBar"])["items"]),
            ([ "navigation", "drawer", "items" ], Obj(navigation["drawer"])["items"]),
            ([ "navigation", "topBar", "actions" ], Obj(navigation["topBar"])["actions"]),
            ([ "linkRules" ], config["linkRules"]),
            ([ "nativeSurfaces" ], config["nativeSurfaces"]),
        ];

        foreach (var (path, list) in lists)
        {
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var items = Arr(list);

            for (var i = 0; i < items.Count; i++)
            {
                if (Str(Obj(items[i])["id"]) is not { } id)
                {
                    continue;
                }

                if (!seen.TryGetValue(id, out var first))
                {
                    seen[id] = i;
                    continue;
                }

                yield return Diagnostic.Create(
                    DiagnosticCode.DuplicateItemId,
                    Severity.Error,
                    JsonPointer.Of([.. path, i, "id"]),
                    $"The id \"{id}\" is already used by item {(first + 1).ToString(CultureInfo.InvariantCulture)} " +
                    "in this list. Every item needs its own id so edits and reordering are tracked correctly.");
            }
        }
    }
}
