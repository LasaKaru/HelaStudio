using System.Globalization;
using System.Text;

namespace Shellwright.Api.Data;

/// <summary>
/// Converts .NET identifiers to the <c>snake_case</c> the database uses.
/// </summary>
/// <remarks>
/// Written out rather than taken as a dependency because it is fifteen lines
/// and because the exact rule matters: it is baked into every migration file
/// the moment the first one is generated, so a package upgrade that changed
/// the rule would rename columns underneath a running system.
/// </remarks>
public static class SnakeCase
{
    /// <summary>Converts a PascalCase or camelCase identifier to snake_case.</summary>
    /// <param name="name">The identifier to convert.</param>
    /// <returns>The snake_case form, or the input unchanged when it is null or empty.</returns>
    public static string Convert(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                // A boundary is a lower-to-upper transition (AppId -> app_id) or
                // the end of an acronym run (SHA256Hash -> sha256_hash).
                var previousIsLower = i > 0 && !char.IsUpper(name[i - 1]) && name[i - 1] != '_';
                var endsAcronym = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (i > 0 && (previousIsLower || endsAcronym))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
