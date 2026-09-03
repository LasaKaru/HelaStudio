using System.Collections.Immutable;
using Shellwright.Orchestrator.Sandbox;

namespace Shellwright.Orchestrator.Activities;

/// <summary>One named command in a build.</summary>
/// <param name="Name">
/// What this step is, in words a customer reading their build log will
/// recognise. It is written into the log ahead of the command's own output,
/// which is the difference between "the build failed" and "the export failed".
/// </param>
/// <param name="Command">What to run.</param>
public sealed record BuildStep(string Name, SandboxCommand Command);

/// <summary>
/// A file the build needs that no template produced.
/// </summary>
/// <remarks>
/// ⚠️ The path is validated on construction rather than where it is written.
/// These paths are constants today, but the type is what a future caller will
/// reach for, and a plan that could name <c>../../.ssh/authorized_keys</c>
/// would turn a build plan into a write primitive pointed at the runner.
/// </remarks>
public sealed record PlannedFile
{
    /// <summary>Creates a planned file.</summary>
    /// <param name="relativePath">Where it goes, relative to the workspace root.</param>
    /// <param name="contents">What to write.</param>
    /// <exception cref="ArgumentException">The path is absolute or escapes the workspace.</exception>
    public PlannedFile(string relativePath, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(contents);

        if (Path.IsPathRooted(relativePath)
            || relativePath.Split('/', '\\').Any(segment => segment == ".."))
        {
            throw new ArgumentException(
                "A planned file must stay inside the workspace.",
                nameof(relativePath));
        }

        RelativePath = relativePath;
        Contents = contents;
    }

    /// <summary>Where it goes, relative to the workspace root.</summary>
    public string RelativePath { get; }

    /// <summary>What to write.</summary>
    public string Contents { get; }
}

/// <summary>
/// Everything one platform's build does, as data.
/// </summary>
/// <param name="Files">Files to write before the first step runs.</param>
/// <param name="Steps">The commands, in order. Each must succeed before the next runs.</param>
/// <param name="ArtifactPath">Where to look for what the build produced.</param>
/// <remarks>
/// <para>
/// ⚠️ A list rather than a single command, because iOS is not a single command:
/// it generates a project, archives it, and exports an IPA from the archive,
/// and each of those fails differently. Collapsing them into one shell
/// invocation would have made the failure mode "exit code 65", which is
/// xcodebuild's way of saying nothing at all.
/// </para>
/// <para>
/// Being data rather than behaviour is what makes the plan testable on Linux.
/// The commands an iOS build would run can be asserted in full — the flags, the
/// order, the environment — on a machine that has never seen a Mac, which is
/// the only way this code was going to be checked at all before hardware
/// exists.
/// </para>
/// </remarks>
public sealed record BuildPlan(
    ImmutableArray<PlannedFile> Files,
    ImmutableArray<BuildStep> Steps,
    string ArtifactPath)
{
    /// <summary>A plan that writes nothing and runs one command.</summary>
    /// <param name="name">What the step is.</param>
    /// <param name="command">What to run.</param>
    /// <param name="artifactPath">Where to look afterwards.</param>
    /// <returns>The plan.</returns>
    public static BuildPlan OneStep(string name, SandboxCommand command, string artifactPath) =>
        new([], [new BuildStep(name, command)], artifactPath);
}
