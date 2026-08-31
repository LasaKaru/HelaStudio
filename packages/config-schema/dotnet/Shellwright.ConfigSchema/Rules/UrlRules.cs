using System.Text.Json.Nodes;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>Parsing helpers shared by the URL rules.</summary>
internal static class Origins
{
    /// <summary>Parses an origin from a URL string, or null if malformed.</summary>
    internal static string? Of(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? $"{uri.Scheme}://{uri.Authority}"
            : null;

    /// <summary>The set of origins the app treats as its own.</summary>
    internal static HashSet<string> Allowed(JsonObject config)
    {
        var origins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Arr(Obj(config["app"])["allowedOrigins"]))
        {
            if (Str(entry) is { } text && Of(text) is { } origin)
            {
                origins.Add(origin);
            }
        }

        return origins;
    }
}

/// <summary>The start URL must be one of the origins the app treats as its own.</summary>
public sealed class InitialUrlAllowedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "initial-url-allowed";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Str(Obj(context.Config["app"])["initialUrl"]) is not { } initialUrl)
        {
            yield break;
        }

        if (Origins.Of(initialUrl) is not { } origin)
        {
            yield break;
        }

        var allowed = Origins.Allowed(context.Config);
        if (allowed.Count == 0 || allowed.Contains(origin))
        {
            yield break;
        }

        yield return Diagnostic.Create(
            DiagnosticCode.InitialUrlNotAllowed,
            Severity.Error,
            JsonPointer.Of("app", "initialUrl"),
            $"The start URL is on {origin}, which is not in your allowed origins. " +
            $"Add \"{origin}\" to allowedOrigins, or point the start URL at an origin you have already listed.");
    }
}

/// <summary>Every internal destination must fall under an allowed origin.</summary>
public sealed class OriginCoverageRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "origin-coverage";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var allowed = Origins.Allowed(context.Config);
        if (allowed.Count == 0)
        {
            yield break;
        }

        foreach (var (path, url) in Destinations(context.Config))
        {
            // A path is resolved against the start URL, so it is covered by definition.
            if (url.StartsWith('/'))
            {
                continue;
            }

            if (Origins.Of(url) is not { } origin || allowed.Contains(origin))
            {
                continue;
            }

            yield return Diagnostic.Create(
                DiagnosticCode.OriginNotCovered,
                Severity.Error,
                path,
                $"This destination is on {origin}, which is not in your allowed origins, so it would open " +
                $"in the device browser instead of inside the app. Add \"{origin}\" to allowedOrigins.");
        }
    }

    private static IEnumerable<(string Path, string Url)> Destinations(JsonObject config)
    {
        var navigation = Obj(config["navigation"]);

        foreach (var container in new[] { "tabBar", "drawer" })
        {
            var items = Arr(Obj(navigation[container])["items"]);
            for (var i = 0; i < items.Count; i++)
            {
                if (Str(Obj(items[i])["url"]) is { } url)
                {
                    yield return (JsonPointer.Of("navigation", container, "items", i, "url"), url);
                }
            }
        }
    }
}

/// <summary>No plain-http URL anywhere: both platforms block cleartext by default.</summary>
public sealed class CleartextUrlRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "cleartext-url";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var found = new List<Diagnostic>();
        foreach (var (key, node) in context.Config)
        {
            // `x-` blocks are opaque extension data, not addresses we route.
            if (key.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }

            WalkStrings(node, [key], (path, _, value) =>
            {
                if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                found.Add(Diagnostic.Create(
                    DiagnosticCode.CleartextUrl,
                    Severity.Error,
                    path,
                    "This is a plain http:// URL. iOS App Transport Security and Android cleartext policy both " +
                    "block it by default, so it would fail to load on a real device. Use https:// instead."));
            });
        }

        return found;
    }
}
