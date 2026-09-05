using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>Metadata about an uploaded asset, as far as validation cares.</summary>
/// <param name="Width">Pixel width of the source image.</param>
/// <param name="Height">Pixel height of the source image.</param>
/// <param name="HasAlpha">Whether the image carries an alpha channel.</param>
public sealed record AssetMetadata(int Width, int Height, bool HasAlpha);

/// <summary>Looks up uploaded assets.</summary>
/// <remarks>
/// Absent in the browser, where assets have not been uploaded yet. Asset rules
/// skip rather than guess, and run again server-side where the store exists.
/// </remarks>
public interface IAssetResolver
{
    /// <summary>Returns metadata for an asset reference, or null if unknown.</summary>
    /// <param name="reference">An <c>asset://sha256-…</c> reference.</param>
    /// <returns>The metadata, or null.</returns>
    AssetMetadata? Lookup(string reference);
}

/// <summary>Everything a rule may consult beyond the document itself.</summary>
/// <param name="Config">The configuration with schema defaults already resolved.</param>
/// <param name="Plugins">Plugins available to this workspace.</param>
/// <param name="Assets">Asset metadata source, when assets have been uploaded.</param>
public sealed record RuleContext(
    JsonObject Config,
    IPluginRegistry Plugins,
    IAssetResolver? Assets = null);

/// <summary>A single semantic check over a configuration document.</summary>
public interface IValidationRule
{
    /// <summary>Stable rule name, used in logs and in the traceability matrix.</summary>
    string Name { get; }

    /// <summary>Returns every finding this rule has about the configuration.</summary>
    /// <param name="context">The document and its surroundings.</param>
    /// <returns>The findings, in any order.</returns>
    IEnumerable<Diagnostic> Run(RuleContext context);
}
