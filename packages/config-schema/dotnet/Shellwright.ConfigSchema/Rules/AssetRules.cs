using System.Globalization;
using System.Text.RegularExpressions;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>Shared asset reference matching.</summary>
internal static partial class AssetReference
{
    internal const int MinIconSize = 1024;

    [GeneratedRegex(@"^asset://sha256-[0-9a-f]{64}$")]
    internal static partial Regex Pattern();
}

/// <summary>Every asset reference must resolve to something that was actually uploaded.</summary>
public sealed class AssetExistsRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "asset-exists";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Assets is not { } assets)
        {
            return [];
        }

        var found = new List<Diagnostic>();
        WalkStrings(context.Config, [], (path, _, value) =>
        {
            if (!AssetReference.Pattern().IsMatch(value) || assets.Lookup(value) is not null)
            {
                return;
            }

            found.Add(Diagnostic.Create(
                DiagnosticCode.AssetMissing,
                Severity.Error,
                path,
                "This file is referenced but is not in your workspace. It may have been deleted, or the " +
                "upload may not have finished. Upload it again."));
        });

        return found;
    }
}

/// <summary>The source icon must be square and large enough for every store density.</summary>
public sealed class IconDimensionsRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "icon-dimensions";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Assets is not { } assets)
        {
            yield break;
        }

        if (Str(Obj(context.Config["branding"])["icon"]) is not { } reference)
        {
            yield break;
        }

        if (assets.Lookup(reference) is not { } metadata)
        {
            yield break;
        }

        if (metadata.Width == metadata.Height && metadata.Width >= AssetReference.MinIconSize)
        {
            yield break;
        }

        var size = AssetReference.MinIconSize.ToString(CultureInfo.InvariantCulture);
        yield return Diagnostic.Create(
            DiagnosticCode.IconDimensions,
            Severity.Error,
            JsonPointer.Of("branding", "icon"),
            $"Your icon is {metadata.Width.ToString(CultureInfo.InvariantCulture)} by " +
            $"{metadata.Height.ToString(CultureInfo.InvariantCulture)} pixels. It must be square and " +
            $"at least {size} by {size}, because every smaller size is generated " +
            "from it and the App Store requires that size for your listing.");
    }
}

/// <summary>iOS rejects an app icon that carries an alpha channel.</summary>
public sealed class IconAlphaRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "icon-alpha";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Assets is not { } assets)
        {
            yield break;
        }

        if (Str(Obj(context.Config["branding"])["icon"]) is not { } reference)
        {
            yield break;
        }

        if (assets.Lookup(reference) is not { HasAlpha: true })
        {
            yield break;
        }

        yield return Diagnostic.Create(
            DiagnosticCode.IconAlpha,
            Severity.Error,
            JsonPointer.Of("branding", "icon"),
            "Your icon has a transparent background. Apple rejects app icons with an alpha channel. " +
            "Flatten it onto a solid background colour and upload it again.");
    }
}
