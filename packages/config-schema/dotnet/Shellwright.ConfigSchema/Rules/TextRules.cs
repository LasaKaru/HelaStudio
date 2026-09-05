using System.Globalization;
using static Shellwright.ConfigSchema.Rules.JsonHelpers;

namespace Shellwright.ConfigSchema.Rules;

/// <summary>
/// Rejects characters that survive validation and then break something
/// downstream.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This rule exists because of a specific, reproducible failure, not out of
/// caution. PostgreSQL's <c>jsonb</c> type cannot represent U+0000 in a string
/// — casting a document containing one raises "unsupported Unicode escape
/// sequence" — so a configuration carrying it passes every check the studio
/// makes, passes the schema, and then fails the save with a 500 that names
/// nothing the author can act on.
/// </para>
/// <para>
/// The other C0 controls store fine and go on to appear verbatim in an Android
/// string resource, an Info.plist, and a store listing. Tab, newline, and
/// carriage return are allowed: multi-line text is legitimate in a description,
/// and the generators escape them correctly.
/// </para>
/// </remarks>
public sealed class NoControlCharactersRule : IValidationRule
{
    /// <inheritdoc/>
    public string Name => "no-control-characters";

    /// <inheritdoc/>
    public IEnumerable<Diagnostic> Run(RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var found = new List<Diagnostic>();

        WalkStrings(context.Config, [], (path, _, value) =>
        {
            foreach (var rune in value.EnumerateRunes())
            {
                var code = rune.Value;
                var isControl = code < 0x20 || (code >= 0x7F && code <= 0x9F);

                if (!isControl || code is 0x09 or 0x0A or 0x0D)
                {
                    continue;
                }

                found.Add(Diagnostic.Create(
                    DiagnosticCode.ControlCharacter,
                    Severity.Error,
                    path,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"This value contains U+{code:X4}, an unprintable control character. ")
                    + "It is almost always an accident of copying and pasting, it cannot be stored, "
                    + "and it would appear verbatim in your store listing. Retype the value rather "
                    + "than pasting it."));

                return;
            }
        });

        return found;
    }
}
