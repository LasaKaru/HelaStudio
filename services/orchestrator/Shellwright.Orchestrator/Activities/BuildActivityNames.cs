namespace Shellwright.Orchestrator.Activities;

/// <summary>
/// The names activities are registered and recorded under.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Explicit, because Temporal's default is derived from the method and that
/// derivation is not stable under refactoring. The SDK strips an <c>Async</c>
/// suffix only from methods that actually return a task — so changing an
/// activity from synchronous to asynchronous, or the reverse, silently renames
/// it from <c>ValidateAsync</c> to <c>Validate</c>.
/// </para>
/// <para>
/// A renamed activity is not a compile error. It is a deploy that leaves every
/// in-flight workflow unable to find the activity it is part-way through, with
/// a failure that says the activity is not registered and lists nine that are.
/// This suite hit exactly that, between a real activity and its test double,
/// which is a cheap place to learn it.
/// </para>
/// </remarks>
public static class BuildActivityNames
{
    /// <summary>Re-runs validation on the server.</summary>
    public const string Validate = "Validate";

    /// <summary>Looks for a reusable artifact.</summary>
    public const string LookupCache = "LookupCache";

    /// <summary>Takes a runner slot.</summary>
    public const string LeaseRunner = "LeaseRunner";

    /// <summary>Generates the platform project.</summary>
    public const string Generate = "Generate";

    /// <summary>Runs the toolchain.</summary>
    public const string Build = "Build";

    /// <summary>Checks what the toolchain produced.</summary>
    public const string Verify = "Verify";

    /// <summary>Stores the artifact.</summary>
    public const string Upload = "Upload";

    /// <summary>Records a state transition.</summary>
    public const string RecordTransition = "RecordTransition";

    /// <summary>Records metered usage.</summary>
    public const string RecordUsage = "RecordUsage";

    /// <summary>Destroys the workspace and frees the slot.</summary>
    public const string ReleaseRunner = "ReleaseRunner";

    /// <summary>Every name, for the test that checks nothing was missed.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Validate,
        LookupCache,
        LeaseRunner,
        Generate,
        Build,
        Verify,
        Upload,
        RecordTransition,
        RecordUsage,
        ReleaseRunner,
    ];
}
