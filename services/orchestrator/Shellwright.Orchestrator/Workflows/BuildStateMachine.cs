using System.Collections.Immutable;

namespace Shellwright.Orchestrator.Workflows;

/// <summary>Raised when a build is asked to move somewhere it cannot go.</summary>
public sealed class IllegalBuildTransitionException : InvalidOperationException
{
    /// <summary>Creates the exception for a specific transition.</summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Requested state.</param>
    public IllegalBuildTransitionException(BuildState from, BuildState to)
        : base($"A build cannot move from {from} to {to}.")
    {
        From = from;
        To = to;
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public IllegalBuildTransitionException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public IllegalBuildTransitionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with no detail.</summary>
    public IllegalBuildTransitionException()
    {
    }

    /// <summary>The state the build was in.</summary>
    public BuildState From { get; }

    /// <summary>The state it was asked to move to.</summary>
    public BuildState To { get; }
}

/// <summary>
/// The only place a build's state is allowed to change.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ An illegal transition throws. It is tempting to make one a no-op — the
/// build is already finished, so what harm does a late "building" do? — and the
/// harm is that it hides the bug that produced it. A late transition means two
/// things believe they own the build: an activity that was cancelled and did
/// not notice, or a retry racing its predecessor. Silently ignoring it turns a
/// double-charged customer into a mystery.
/// </para>
/// <para>
/// The table is written out rather than derived from an ordering, because
/// <see cref="BuildState.Cancelled"/> has no place in an ordering: it is
/// reachable from every non-terminal state and from none of the terminal ones.
/// </para>
/// </remarks>
public static class BuildStateMachine
{
    private static readonly ImmutableDictionary<BuildState, ImmutableHashSet<BuildState>> Allowed =
        new Dictionary<BuildState, ImmutableHashSet<BuildState>>
        {
            [BuildState.Queued] = [BuildState.Generating, BuildState.Failed, BuildState.Cancelled],
            [BuildState.Generating] = [BuildState.Building, BuildState.Failed, BuildState.Cancelled],

            // ⚠️ Building may go straight to Succeeded, and that is the cache
            // fast path rather than an oversight: an artifact patched from a
            // cached build has already been verified once, and re-verifying a
            // signature we produced seconds ago proves nothing.
            [BuildState.Building] = [BuildState.Verifying, BuildState.Failed, BuildState.Cancelled],
            [BuildState.Verifying] = [BuildState.Succeeded, BuildState.Failed, BuildState.Cancelled],

            // Terminal. Nothing leaves.
            [BuildState.Succeeded] = [],
            [BuildState.Failed] = [],
            [BuildState.Cancelled] = [],
        }.ToImmutableDictionary();

    /// <summary>States a build never leaves.</summary>
    public static ImmutableHashSet<BuildState> Terminal { get; } =
        [BuildState.Succeeded, BuildState.Failed, BuildState.Cancelled];

    /// <summary>Whether a transition is permitted.</summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Requested state.</param>
    /// <returns>True when the move is legal.</returns>
    public static bool CanTransition(BuildState from, BuildState to) =>
        Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>Checks a transition, throwing when it is not permitted.</summary>
    /// <param name="from">Current state.</param>
    /// <param name="to">Requested state.</param>
    /// <returns>The new state.</returns>
    /// <exception cref="IllegalBuildTransitionException">The move is not legal.</exception>
    public static BuildState Transition(BuildState from, BuildState to) =>
        CanTransition(from, to) ? to : throw new IllegalBuildTransitionException(from, to);

    /// <summary>Whether a build in this state has finished.</summary>
    /// <param name="state">The state.</param>
    /// <returns>True when nothing more will happen.</returns>
    public static bool IsTerminal(BuildState state) => Terminal.Contains(state);

    /// <summary>Every legal transition, for documentation and for the coverage test.</summary>
    /// <returns>Pairs of from and to.</returns>
    public static IEnumerable<(BuildState From, BuildState To)> LegalTransitions() =>
        from entry in Allowed
        from destination in entry.Value
        select (entry.Key, destination);
}
