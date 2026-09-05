using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Shellwright.ConfigSchema.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void Sorts_object_keys_by_code_unit()
    {
        var node = JsonNode.Parse("""{"b":1,"a":2,"A":3}""");
        CanonicalJson.Serialize(node).Should().Be("""{"A":3,"a":2,"b":1}""");
    }

    [Fact]
    public void Omits_null_valued_keys_so_absent_and_explicit_null_agree()
    {
        CanonicalJson.Serialize(JsonNode.Parse("""{"a":1,"b":null}"""))
            .Should()
            .Be(CanonicalJson.Serialize(JsonNode.Parse("""{"a":1}""")));
    }

    [Fact]
    public void Preserves_array_order_and_array_nulls()
    {
        CanonicalJson.Serialize(JsonNode.Parse("[3,null,1]")).Should().Be("[3,null,1]");
    }

    [Theory]
    [InlineData(1.0, "1")]
    [InlineData(-0.0, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(1e21, "1E21")]
    [InlineData(1e-7, "1E-7")]
    [InlineData(42, "42")]
    public void Formats_numbers_in_shortest_round_trip_form(double value, string expected)
    {
        CanonicalJson.FormatNumber(value).Should().Be(expected);
    }

    [Fact]
    public void Rejects_non_finite_numbers_rather_than_emitting_invalid_json()
    {
        var act = () => CanonicalJson.FormatNumber(double.NaN);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Normalises_strings_to_nfc_so_composed_and_decomposed_forms_agree()
    {
        const string Composed = "caf\u00e9";
        const string Decomposed = "cafe\u0301";
        CanonicalJson.EscapeString(Decomposed).Should().Be(CanonicalJson.EscapeString(Composed));
    }

    [Fact]
    public void Escapes_quotes_backslashes_and_control_characters()
    {
        CanonicalJson.EscapeString("a\"b\\c\nd\u0001")
            .Should()
            .Be("\"a\\\"b\\\\c\\nd\\u0001\"");
    }

    [Fact]
    public void Leaves_emoji_and_non_latin_scripts_unescaped()
    {
        const string Label = "\U0001F3E0 \u0627\u0644\u0631\u0626\u064A\u0633\u064A\u0629";
        CanonicalJson.EscapeString(Label).Should().Be($"\"{Label}\"");
    }

    [Fact]
    public void Serialises_nodes_built_in_code_as_well_as_nodes_parsed_from_text()
    {
        // The hash projections build nodes in code, which hold CLR values rather
        // than JsonElements. Both paths must produce the same bytes.
        var built = new JsonObject { ["a"] = true, ["b"] = "x", ["c"] = 1 };
        var parsed = JsonNode.Parse("""{"a":true,"b":"x","c":1}""");

        CanonicalJson.Serialize(built).Should().Be(CanonicalJson.Serialize(parsed));
    }

    /// <summary>
    /// Key order must never change the bytes, over the whole fixture corpus.
    /// </summary>
    /// <remarks>
    /// The TypeScript side runs this as a generated property test over a thousand
    /// cases. Here it runs over the real corpus instead, which is the input that
    /// actually matters: these are the documents whose hashes gate every build.
    /// </remarks>
    [Fact]
    public void Is_order_independent_across_the_fixture_corpus()
    {
        foreach (var name in Fixtures.ListConfigs())
        {
            var config = Fixtures.ReadConfig(name);

            CanonicalJson.Serialize(ReverseKeys(config))
                .Should()
                .Be(CanonicalJson.Serialize(config), $"key order must not change the bytes of {name}");
        }
    }

    /// <summary>Rebuilds a document with every object's keys in the opposite order.</summary>
    private static JsonNode? ReverseKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                {
                    var reversed = new JsonObject();
                    foreach (var (key, value) in obj.Reverse())
                    {
                        reversed[key] = ReverseKeys(value);
                    }

                    return reversed;
                }

            case JsonArray array:
                {
                    var copy = new JsonArray();
                    foreach (var item in array)
                    {
                        copy.Add(ReverseKeys(item));
                    }

                    return copy;
                }

            default:
                return node?.DeepClone();
        }
    }
}
