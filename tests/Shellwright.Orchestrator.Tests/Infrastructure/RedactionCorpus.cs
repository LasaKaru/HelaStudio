using System.Text.Json;

namespace Shellwright.Orchestrator.Tests.Infrastructure;

/// <summary>One case from the redaction corpus.</summary>
/// <param name="Name">What the tool was doing.</param>
/// <param name="Line">The line it printed.</param>
/// <param name="MustNotContain">Values that must not survive redaction.</param>
/// <param name="MustContain">Values that must survive it.</param>
public sealed record RedactionCase(
    string Name,
    string Line,
    IReadOnlyList<string> MustNotContain,
    IReadOnlyList<string>? MustContain);

/// <summary>
/// The leaky build output every redaction test is measured against.
/// </summary>
/// <remarks>
/// ⚠️ One corpus, loaded from one file, shared by the tests that check the
/// redactor and the tests that check the pipeline built on it. Keeping the
/// credential shapes in a fixture rather than in source is also what lets the
/// secret scanner allowlist exactly one path: a plausible-looking key inlined
/// in a <c>.cs</c> file would either trip the scanner on every commit or force
/// an allowlist entry broad enough to hide a real one.
/// </remarks>
public static class RedactionCorpus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<IReadOnlyList<RedactionCase>> Loaded = new(Load);

    /// <summary>Every case in the corpus.</summary>
    public static IReadOnlyList<RedactionCase> Cases => Loaded.Value;

    /// <summary>Finds one case by name.</summary>
    /// <param name="name">The case's <see cref="RedactionCase.Name"/>.</param>
    /// <returns>The case.</returns>
    public static RedactionCase Case(string name) =>
        Cases.FirstOrDefault(entry => entry.Name == name)
        ?? throw new InvalidOperationException(
            $"No case named '{name}' in the redaction corpus. Renaming a case breaks the tests "
            + "that name it, which is the intended way to notice.");

    private static List<RedactionCase> Load()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Shellwright.slnx")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(
            root!.FullName,
            "tests",
            "fixtures",
            "log-redaction",
            "leaky-output.json");

        return JsonSerializer.Deserialize<List<RedactionCase>>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"The redaction corpus at {path} did not parse.");
    }
}
