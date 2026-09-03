using System.Collections.Immutable;
using Temporalio.Exceptions;

namespace Shellwright.Orchestrator.Workflows;

/// <summary>
/// The failure types the retry policy distinguishes.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This split is the difference between a resilient system and an expensive
/// one. Infrastructure failures — a runner that will not answer, R2 returning
/// 503 — are transient and worth retrying. A compilation failure is not: the
/// same inputs will fail identically, and each attempt costs runner minutes
/// somebody is paying for.
/// </para>
/// <para>
/// Temporal decides by matching the error *type* string, so these constants are
/// the contract between the throw site and the retry policy. A typo in either
/// silently turns a non-retryable failure back into three of them, which is why
/// they are constants here rather than literals at either end, and why
/// <c>BuildWorkflowTests</c> asserts a compilation failure is attempted once.
/// </para>
/// </remarks>
public static class BuildFailures
{
    /// <summary>The configuration did not validate. Retrying cannot help.</summary>
    public const string ConfigInvalid = "ConfigInvalid";

    /// <summary>The toolchain rejected the generated project. Retrying cannot help.</summary>
    public const string CompilationFailed = "CompilationFailed";

    /// <summary>The artifact failed signature, manifest, or budget checks. Retrying cannot help.</summary>
    public const string VerificationFailed = "VerificationFailed";

    /// <summary>No runner could be leased. Worth retrying.</summary>
    public const string RunnerUnavailable = "RunnerUnavailable";

    /// <summary>Object storage or another dependency failed. Worth retrying.</summary>
    public const string StorageUnavailable = "StorageUnavailable";

    /// <summary>
    /// This deployment cannot build for that platform at all. Retrying cannot help.
    /// </summary>
    /// <remarks>
    /// ⚠️ Distinct from <see cref="RunnerUnavailable"/>, and the distinction is
    /// the whole reason it exists. "No runner is free" is a condition that
    /// passes in a minute and is worth waiting for; "there is no macOS fleet" or
    /// "no Apple team is configured" does not pass, and telling a customer their
    /// build is queued for a runner that will never exist is a lie that costs
    /// them twenty minutes before it fails anyway.
    /// </remarks>
    public const string PlatformUnavailable = "PlatformUnavailable";

    /// <summary>Every failure a retry cannot fix.</summary>
    public static ImmutableArray<string> NonRetryable { get; } =
        [ConfigInvalid, CompilationFailed, VerificationFailed, PlatformUnavailable];

    /// <summary>Builds a failure that Temporal will not retry.</summary>
    /// <param name="type">One of the non-retryable type names.</param>
    /// <param name="message">What a person can do about it.</param>
    /// <returns>The exception to throw from an activity.</returns>
    public static ApplicationFailureException Permanent(string type, string message) =>
        new(message, type, nonRetryable: true);

    /// <summary>Builds a failure Temporal should retry.</summary>
    /// <param name="type">One of the retryable type names.</param>
    /// <param name="message">What went wrong.</param>
    /// <returns>The exception to throw from an activity.</returns>
    public static ApplicationFailureException Transient(string type, string message) =>
        new(message, type);
}
