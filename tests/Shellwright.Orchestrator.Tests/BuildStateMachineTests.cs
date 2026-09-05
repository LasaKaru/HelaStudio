using FluentAssertions;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// Every legal transition, and every illegal one.
/// </summary>
/// <remarks>
/// ⚠️ The illegal cases are enumerated exhaustively rather than sampled,
/// because the failure they guard against is silent. A late "building" arriving
/// after a build finished means two things believe they own it — a cancelled
/// activity that did not notice, or a retry racing its predecessor — and a
/// state machine that shrugged would turn a double-charged customer into a
/// mystery.
/// </remarks>
public sealed class BuildStateMachineTests
{
    [Theory]
    [InlineData(BuildState.Queued, BuildState.Generating)]
    [InlineData(BuildState.Queued, BuildState.Failed)]
    [InlineData(BuildState.Queued, BuildState.Cancelled)]
    [InlineData(BuildState.Generating, BuildState.Building)]
    [InlineData(BuildState.Generating, BuildState.Failed)]
    [InlineData(BuildState.Generating, BuildState.Cancelled)]
    [InlineData(BuildState.Building, BuildState.Verifying)]
    [InlineData(BuildState.Building, BuildState.Failed)]
    [InlineData(BuildState.Building, BuildState.Cancelled)]
    [InlineData(BuildState.Verifying, BuildState.Succeeded)]
    [InlineData(BuildState.Verifying, BuildState.Failed)]
    [InlineData(BuildState.Verifying, BuildState.Cancelled)]
    public void Legal_transitions_are_allowed(BuildState from, BuildState to)
    {
        BuildStateMachine.CanTransition(from, to).Should().BeTrue();
        BuildStateMachine.Transition(from, to).Should().Be(to);
    }

    /// <summary>Everything not in the table is refused, and refused loudly.</summary>
    [Fact]
    public void Every_other_transition_throws()
    {
        var legal = BuildStateMachine.LegalTransitions().ToHashSet();
        var refused = 0;

        foreach (var from in Enum.GetValues<BuildState>())
        {
            foreach (var to in Enum.GetValues<BuildState>())
            {
                if (legal.Contains((from, to)))
                {
                    continue;
                }

                var move = () => BuildStateMachine.Transition(from, to);
                move.Should().Throw<IllegalBuildTransitionException>(
                    "moving from {0} to {1} is not in the table", from, to);

                refused++;
            }
        }

        // 49 pairs in total, 12 of them legal.
        refused.Should().Be(37);
    }

    /// <summary>A build never leaves a terminal state, including to itself.</summary>
    [Theory]
    [InlineData(BuildState.Succeeded)]
    [InlineData(BuildState.Failed)]
    [InlineData(BuildState.Cancelled)]
    public void Terminal_states_are_final(BuildState terminal)
    {
        BuildStateMachine.IsTerminal(terminal).Should().BeTrue();

        foreach (var to in Enum.GetValues<BuildState>())
        {
            BuildStateMachine.CanTransition(terminal, to).Should().BeFalse();
        }
    }

    /// <summary>Cancellation is reachable from every state a build can still be running in.</summary>
    [Fact]
    public void Cancellation_is_reachable_from_every_running_state()
    {
        var running = Enum.GetValues<BuildState>().Where(x => !BuildStateMachine.IsTerminal(x));

        foreach (var state in running)
        {
            BuildStateMachine.CanTransition(state, BuildState.Cancelled).Should().BeTrue(
                "a build in {0} must be cancellable, or a runner is held until it times out", state);
        }
    }

    /// <summary>Failure is reachable from every state a build can still be running in.</summary>
    [Fact]
    public void Failure_is_reachable_from_every_running_state()
    {
        var running = Enum.GetValues<BuildState>().Where(x => !BuildStateMachine.IsTerminal(x));

        foreach (var state in running)
        {
            BuildStateMachine.CanTransition(state, BuildState.Failed).Should().BeTrue();
        }
    }

    /// <summary>The happy path is a single chain with no shortcuts.</summary>
    [Fact]
    public void The_happy_path_visits_every_stage()
    {
        var state = BuildState.Queued;

        foreach (var next in new[]
        {
            BuildState.Generating,
            BuildState.Building,
            BuildState.Verifying,
            BuildState.Succeeded,
        })
        {
            state = BuildStateMachine.Transition(state, next);
        }

        state.Should().Be(BuildState.Succeeded);

        // ⚠️ No shortcut to succeeded. Verification is what stands between a
        // compiler's output and a customer's download.
        BuildStateMachine.CanTransition(BuildState.Building, BuildState.Succeeded).Should().BeFalse();
        BuildStateMachine.CanTransition(BuildState.Queued, BuildState.Succeeded).Should().BeFalse();
    }
}
