using System.Text.Json.Nodes;

namespace Shellwright.ConfigSchema.Tests;

/// <summary>Shared access to the fixture corpus in <c>tests/fixtures</c>.</summary>
internal static class Fixtures
{
    /// <summary>Root of the shared fixture corpus.</summary>
    internal static string Root { get; } = FindRoot();

    internal static string ConfigDir => Path.Combine(Root, "configs");

    internal static string MigrationDir => Path.Combine(Root, "migrations");

    internal static string ExpectedDir => Path.Combine(Root, "expected");

    internal static string RegexSafetyDir => Path.Combine(Root, "regex-safety");

    /// <summary>Reads one fixture config by file name.</summary>
    internal static JsonObject ReadConfig(string name) => ReadJson(Path.Combine(ConfigDir, name));

    /// <summary>Reads one migration fixture by file name.</summary>
    internal static JsonObject ReadMigration(string name) => ReadJson(Path.Combine(MigrationDir, name));

    /// <summary>Reads one golden file by name.</summary>
    internal static JsonObject ReadExpected(string name) => ReadJson(Path.Combine(ExpectedDir, name));

    /// <summary>Every fixture file name matching a prefix, sorted.</summary>
    internal static IEnumerable<string> ListConfigs(string prefix = "") =>
        Directory.EnumerateFiles(ConfigDir, "*.json")
            .Select(Path.GetFileName)
            .Where(name => name is not null && name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal);

    private static JsonObject ReadJson(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException($"Not a JSON object: {path}");

    /// <summary>
    /// Walks up from the test assembly to the repository's fixture directory.
    /// </summary>
    /// <remarks>
    /// The corpus is shared with the TypeScript suite and so cannot be copied into
    /// the output directory — both implementations must read the same bytes, or
    /// the contract test would be comparing two copies rather than one truth.
    /// </remarks>
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tests/fixtures from the test assembly.");
    }
}
