using System.Reflection;

namespace Shellwright.Api.Data;

/// <summary>
/// Reads the hand-written SQL that migrations apply.
/// </summary>
/// <remarks>
/// The alternative is a two-hundred-line verbatim string literal inside a
/// migration class, which no one reviews, no editor highlights, and no diff
/// reads cleanly. Keeping the SQL in <c>.sql</c> files costs one indirection
/// and makes the security-critical part of the schema legible.
/// </remarks>
public static class SqlResource
{
    private const string Prefix = "Shellwright.Api.Data.Sql.";

    /// <summary>Reads an embedded SQL script by file name.</summary>
    /// <param name="name">File name, such as <c>RowLevelSecurity.up.sql</c>.</param>
    /// <returns>The script text.</returns>
    /// <exception cref="InvalidOperationException">The script is not embedded in the assembly.</exception>
    public static string Read(string name)
    {
        var assembly = typeof(SqlResource).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + name)
            ?? throw new InvalidOperationException(
                $"SQL resource '{name}' is not embedded. Check the EmbeddedResource glob in Shellwright.Api.csproj.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
