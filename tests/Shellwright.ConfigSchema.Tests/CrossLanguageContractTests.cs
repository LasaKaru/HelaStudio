using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Shellwright.ConfigSchema.Tests;

/// <summary>
/// The contract between the two validators.
/// </summary>
/// <remarks>
/// <para>
/// The TypeScript engine runs in the browser on every keystroke; this one runs at
/// the API and on the build runner. If they disagree, a customer sees a config
/// save cleanly and then fail at build time — or, worse, two identical configs
/// hash differently and the build cache silently stops working.
/// </para>
/// <para>
/// Both implementations assert against the same committed golden files, produced
/// by <c>packages/config-schema/scripts/write-goldens.ts</c>. Either side drifting
/// fails CI. This test is the single defence named in SPRINT-01 T-01.3, and is
/// worth more than either implementation's own unit tests.
/// </para>
/// </remarks>
public sealed class CrossLanguageContractTests
{
    private static readonly HashContext Context = new(
        "1.0.0",
        new Dictionary<string, string> { ["qr-scanner"] = "1.2.0" },
        new Dictionary<string, string> { ["agp"] = "8.9", ["xcode"] = "26.1" });

    public static TheoryData<string> AllFixtures()
    {
        var data = new TheoryData<string>();
        foreach (var name in Fixtures.ListConfigs())
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>TC-S01-CFG-011 — diagnostics agree exactly across both languages.</summary>
    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Diagnostics_match_the_shared_golden(string name)
    {
        var expected = Fixtures.ReadExpected("diagnostics.json")[name];
        expected.Should().NotBeNull($"the golden file must cover every fixture, and is missing {name}");

        var actual = Serialise(new ConfigValidator().Validate(Fixtures.ReadConfig(name)).Result);

        CanonicalJson.Serialize(actual)
            .Should()
            .Be(CanonicalJson.Serialize(expected), $"C# and TypeScript must agree on {name}");
    }

    /// <summary>TC-S01-CFG-012 — canonical bytes agree exactly across both languages.</summary>
    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Canonical_form_matches_the_shared_golden(string name)
    {
        var expected = Fixtures.ReadExpected("canonical.json")[name]!.GetValue<string>();
        var resolved = new ConfigValidator().Validate(Fixtures.ReadConfig(name)).Resolved;

        CanonicalJson.Serialize(resolved).Should().Be(expected);
    }

    /// <summary>TC-S01-CFG-057 — the three cache keys agree exactly across both languages.</summary>
    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Hashes_match_the_shared_golden(string name)
    {
        var expected = Fixtures.ReadExpected("hashes.json")[name];
        var validated = new ConfigValidator().Validate(Fixtures.ReadConfig(name));

        if (!validated.Result.Valid)
        {
            // A document that fails validation is never built, so it has no keys.
            expected.Should().BeNull($"{name} is invalid and should not be hashed");
            return;
        }

        expected.Should().NotBeNull($"the golden file is missing hashes for the valid fixture {name}");

        var actual = ConfigHasher.Compute(validated.Resolved, Context);
        actual.CodeKey.Should().Be(expected!["codeKey"]!.GetValue<string>());
        actual.AssetKey.Should().Be(expected["assetKey"]!.GetValue<string>());
        actual.ContentKey.Should().Be(expected["contentKey"]!.GetValue<string>());
    }

    /// <summary>Serialises a result into the same JSON shape the TypeScript side emits.</summary>
    private static JsonObject Serialise(ValidationResult result) => new()
    {
        ["valid"] = result.Valid,
        ["errors"] = Bucket(result.Errors),
        ["warnings"] = Bucket(result.Warnings),
        ["info"] = Bucket(result.Info),
    };

    private static JsonArray Bucket(IEnumerable<Diagnostic> diagnostics)
    {
        var array = new JsonArray();
        foreach (var diagnostic in diagnostics)
        {
            array.Add(new JsonObject
            {
                ["code"] = diagnostic.Code,
                ["severity"] = JsonNamingPolicy.CamelCase.ConvertName(diagnostic.Severity.ToString()),
                ["path"] = diagnostic.Path,
                ["message"] = diagnostic.Message,
                ["docsUrl"] = diagnostic.DocsUrl,
            });
        }

        return array;
    }
}
