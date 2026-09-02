using FluentAssertions;
using Xunit;
using ApiBuildCacheOutcome = Shellwright.Api.Domain.BuildCacheOutcome;
using ApiBuildPlatform = Shellwright.Api.Domain.BuildPlatform;
using ApiBuildState = Shellwright.Api.Domain.BuildState;
using ApiBuildType = Shellwright.Api.Domain.BuildType;
using RunnerBuildPlatform = Shellwright.Orchestrator.Workflows.BuildPlatform;
using RunnerBuildState = Shellwright.Orchestrator.Workflows.BuildState;
using RunnerBuildType = Shellwright.Orchestrator.Workflows.BuildType;
using RunnerCacheOutcome = Shellwright.Orchestrator.Workflows.CacheOutcome;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-090–093 — the two services agree on the numbers they exchange.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ The enums are declared twice on purpose: the API and the orchestrator
/// deploy separately and version separately, and a shared type would make a
/// rename in one a silent wire-format change in the other. What makes that safe
/// rather than reckless is this file.
/// </para>
/// <para>
/// ⚠️ The orchestrator writes these values straight into the API's columns as
/// integers. A divergence therefore does not fail — it reinterprets. The first
/// version of the API's <c>BuildState</c> invented two extra states and
/// renumbered the terminal ones, which would have left every successful build
/// with no finish time and recorded every cancelled build as succeeded, with no
/// error anywhere. This test is what catches that.
/// </para>
/// </remarks>
public sealed class BuildContractTests
{
    [Fact(DisplayName = "BuildState means the same thing in both services")]
    public void BuildStatesAgree() =>
        AssertSameShape<ApiBuildState, RunnerBuildState>();

    [Fact(DisplayName = "BuildPlatform means the same thing in both services")]
    public void BuildPlatformsAgree() =>
        AssertSameShape<ApiBuildPlatform, RunnerBuildPlatform>();

    [Fact(DisplayName = "BuildType means the same thing in both services")]
    public void BuildTypesAgree() =>
        AssertSameShape<ApiBuildType, RunnerBuildType>();

    [Fact(DisplayName = "The cache outcome means the same thing in both services")]
    public void CacheOutcomesAgree() =>
        AssertSameShape<ApiBuildCacheOutcome, RunnerCacheOutcome>();

    /// <summary>
    /// Asserts two enums have identical names and identical numbers.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Equal names with different numbers is the silent
    /// reinterpretation above; equal numbers with different names is a rename
    /// that will confuse whoever reads a row six months from now.
    /// </remarks>
    /// <typeparam name="TLeft">One side's enum.</typeparam>
    /// <typeparam name="TRight">The other side's.</typeparam>
    private static void AssertSameShape<TLeft, TRight>()
        where TLeft : struct, Enum
        where TRight : struct, Enum
    {
        var left = Describe<TLeft>();
        var right = Describe<TRight>();

        right.Should().Equal(
            left,
            "{0} and {1} are exchanged as integers, so they must agree name for name and number "
            + "for number",
            typeof(TLeft).FullName,
            typeof(TRight).FullName);
    }

    private static SortedDictionary<int, string> Describe<TEnum>()
        where TEnum : struct, Enum
    {
        var described = new SortedDictionary<int, string>();

        foreach (var value in Enum.GetValues<TEnum>())
        {
            described[Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)] =
                value.ToString()!;
        }

        return described;
    }
}
