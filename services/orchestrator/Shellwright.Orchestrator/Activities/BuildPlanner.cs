using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Fleet;
using Shellwright.Orchestrator.Workflows;

namespace Shellwright.Orchestrator.Activities;

/// <summary>
/// Turns a build request into the commands that produce its artifact.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Pure. It reads configuration and returns data; it touches no disk and
/// starts no process. That is what lets an iOS build's every flag be asserted
/// on a Linux CI runner, which is the only review this code can get until there
/// is a Mac to run it on.
/// </para>
/// <para>
/// The platform switch has no default arm that guesses. A platform nobody has
/// written a plan for must fail loudly at the point of planning, rather than
/// silently inherit Android's Gradle invocation and spend twenty minutes
/// failing to find a <c>gradlew</c>.
/// </para>
/// </remarks>
/// <param name="options">This deployment's iOS settings.</param>
public sealed class BuildPlanner(IOptions<IosBuildOptions> options)
{
    /// <summary>
    /// The Xcode scheme an iOS build archives.
    /// </summary>
    /// <remarks>
    /// ⚠️ A constant, and that is a property of the shell rather than an
    /// assumption about it: <c>shells/ios/templates/project.yml.tmpl</c> names
    /// its single application target <c>Shellwright</c> and XcodeGen derives
    /// the scheme from the target, so the scheme does not vary with the
    /// customer's app name. <c>BuildPlannerTests</c> reads that template and
    /// asserts it, because a drift here is a build that fails at the archive
    /// step with "scheme not found" and no clue why.
    ///
    /// It also means no customer-controlled text reaches the <c>-scheme</c>
    /// argument, which is one fewer place an app named <c>Foo"; rm -rf ~ #</c>
    /// could matter.
    /// </remarks>
    public const string IosScheme = "Shellwright";

    /// <summary>Where an iOS build writes its export options, relative to the workspace.</summary>
    public const string IosExportOptionsPath = "build/ExportOptions.plist";

    private readonly IosBuildOptions ios = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Plans a build.</summary>
    /// <param name="request">What to build.</param>
    /// <param name="lease">The runner slot it holds.</param>
    /// <param name="project">What generation produced.</param>
    /// <returns>The commands, in order.</returns>
    /// <exception cref="NotSupportedException">No plan exists for the request's platform.</exception>
    public BuildPlan Plan(BuildRequest request, RunnerLease lease, GeneratedProject project)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(project);

        return request.Platform switch
        {
            BuildPlatform.Android => BuildPlan.OneStep(
                "Compiling",
                BuildCommands.For(request, project),
                BuildCommands.ArtifactPath(request, project)),
            BuildPlatform.Ios => PlanIos(request, lease, project),
            _ => throw new NotSupportedException(
                $"No build plan exists for {request.Platform}."),
        };
    }

    private BuildPlan PlanIos(BuildRequest request, RunnerLease lease, GeneratedProject project)
    {
        // ⚠️ Refused at planning time rather than at export time. Without a
        // team there is nothing to sign for, and xcodebuild discovers that
        // only after the archive — so the alternative is a twenty-minute build
        // that ends in a signing error naming a setting the customer cannot
        // see and did not choose.
        var teamId = ios.TeamId
            ?? throw new InvalidOperationException(
                "No Apple team is configured, so an iOS archive cannot be exported. "
                + $"Set {IosBuildOptions.SectionName}:{nameof(IosBuildOptions.TeamId)}.");

        var toolchain = ios.Toolchain();
        var buildRoot = Path.Combine(lease.WorkspaceRoot, "build");
        var archivePath = Path.Combine(buildRoot, IosScheme + ".xcarchive");
        var exportPath = Path.Combine(buildRoot, "export");

        // Derived from the same constant the planned file uses, so the plist
        // xcodebuild is told to read cannot drift from the plist we write.
        var exportOptionsPath = Path.Combine(lease.WorkspaceRoot, IosExportOptionsPath);

        // ⚠️ Under the app's cache root rather than the workspace. DerivedData
        // is the expensive half of an incremental Xcode build and is worth
        // keeping between builds of the same app — and it is writable, which is
        // exactly why it must never be the *shared* root: one tenant's
        // DerivedData is a place to leave something another tenant's build
        // would link against.
        var derivedDataPath = Path.Combine(lease.CacheRoot, "DerivedData");

        var steps = ImmutableArray.Create(
            // First, and unconditionally. When a submission is rejected for a
            // toolchain reason this line is the first thing anyone will want,
            // and reconstructing it after the fact is impossible.
            new BuildStep(
                "Reporting the Xcode version",
                IosBuildCommands.ReportVersion(toolchain, project.ProjectRoot)),
            new BuildStep(
                "Generating the Xcode project",
                IosBuildCommands.Generate(toolchain, project.ProjectRoot)),
            new BuildStep(
                "Archiving",
                IosBuildCommands.Archive(
                    toolchain,
                    IosScheme,
                    project.ProjectRoot,
                    archivePath,
                    derivedDataPath,
                    request.Type)),
            new BuildStep(
                "Exporting the IPA",
                IosBuildCommands.Export(
                    toolchain,
                    archivePath,
                    exportOptionsPath,
                    exportPath,
                    project.ProjectRoot)));

        return new BuildPlan(
            [new PlannedFile(IosExportOptionsPath, IosBuildCommands.ExportOptions(ios.ExportMethod, teamId))],
            steps,
            exportPath);
    }
}
