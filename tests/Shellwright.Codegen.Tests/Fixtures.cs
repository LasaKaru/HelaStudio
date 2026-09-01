using System.Text.Json.Nodes;
using Shellwright.Codegen;
using Shellwright.ConfigSchema;

namespace Shellwright.Codegen.Tests;

/// <summary>Shared access to the repository, its fixtures, and the shell template.</summary>
internal static class Fixtures
{
    /// <summary>The repository root.</summary>
    internal static string RepoRoot { get; } = FindRoot();

    /// <summary>The Android shell, which is also the Android template.</summary>
    internal static string AndroidShell => Path.Combine(RepoRoot, "shells", "android");

    /// <summary>Directory holding <c>appconfig</c> fixtures.</summary>
    internal static string ConfigDir => Path.Combine(RepoRoot, "tests", "fixtures", "configs");

    /// <summary>Directory holding approved generated-project snapshots.</summary>
    internal static string GoldenDir => Path.Combine(RepoRoot, "tests", "fixtures", "generated");

    /// <summary>A generator reading the in-tree Android shell.</summary>
    internal static Android.AndroidProjectGenerator Generator() => new(new TemplateSource(AndroidShell));

    /// <summary>Reads a fixture config and resolves its schema defaults.</summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>The resolved configuration.</returns>
    internal static JsonObject Resolve(string name)
    {
        var json = File.ReadAllText(Path.Combine(ConfigDir, name));
        return Resolve(JsonNode.Parse(json)!);
    }

    /// <summary>Resolves schema defaults for an already-parsed config.</summary>
    /// <param name="config">The raw configuration.</param>
    /// <returns>The resolved configuration.</returns>
    internal static JsonObject Resolve(JsonNode config)
    {
        var validated = new ConfigValidator().Validate(config);

        // Generation from an invalid config is not a scenario: the API refuses
        // long before it gets here. A fixture that fails to validate is a
        // broken fixture, and saying so loudly beats a confusing render error.
        var errors = validated.Result.Errors;

        return errors.Length == 0
            ? validated.Resolved
            : throw new InvalidOperationException(
                "Fixture does not validate: " + string.Join("; ", errors.Select(e => $"{e.Code} at {e.Path}")));
    }

    /// <summary>The shell's own config — the one the committed files are rendered from.</summary>
    internal static JsonObject ShellConfig()
    {
        var path = Path.Combine(AndroidShell, "app", "src", "main", "assets", "appconfig.json");
        return Resolve(JsonNode.Parse(File.ReadAllText(path))!);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test assembly.");
    }
}
