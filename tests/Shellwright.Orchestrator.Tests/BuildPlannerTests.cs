using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Codegen;
using Shellwright.ConfigSchema;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Fleet;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S08-BLD-029–046 — what each platform's build actually runs, and what its
/// cache key says about the toolchain that ran it.
/// </summary>
/// <remarks>
/// ⚠️ Every assertion here is on data. <see cref="BuildPlanner"/> starts no
/// process and touches no disk, which is the only reason an iOS build's flags
/// can be reviewed at all on a machine that has never seen a Mac. What these
/// tests cannot tell you is whether xcodebuild accepts the flags — that needs
/// hardware, and the gap is recorded in <c>ACTION_REQUIRED.md</c> rather than
/// papered over here.
/// </remarks>
public sealed class BuildPlannerTests
{
    private const string TeamId = "AB12CD34EF";

    private static readonly RunnerLease Lease = new(
        LeaseId: "lease-1",
        RunnerId: "runner-1",
        WorkspaceRoot: Path.Combine(Path.GetTempPath(), "workspace"),
        CacheRoot: Path.Combine(Path.GetTempPath(), "cache"));

    private static readonly GeneratedProject Project = new(
        Path.Combine(Path.GetTempPath(), "workspace", "project"),
        FileCount: 42);

    [Fact]
    public void AndroidPlanIsOneGradleInvocation()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Android, BuildType.Release), Lease, Project);

        plan.Steps.Should().ContainSingle();
        plan.Steps[0].Command.Executable.Should().Be("./gradlew");
        plan.Steps[0].Command.Arguments.Should().Contain("assembleRelease");
        plan.Files.Should().BeEmpty();
        plan.ArtifactPath.Should().EndWith(Path.Combine("apk", "release"));
    }

    [Fact]
    public void AndroidDebugBuildsTheDebugVariant()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Android, BuildType.Debug), Lease, Project);

        plan.Steps[0].Command.Arguments.Should().Contain("assembleDebug");
        plan.ArtifactPath.Should().EndWith(Path.Combine("apk", "debug"));
    }

    [Fact]
    public void IosPlanReportsTheToolchainGeneratesArchivesAndExports()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        plan.Steps.Select(step => step.Command.Executable)
            .Should().Equal("xcodebuild", "xcodegen", "xcodebuild", "xcodebuild");

        plan.Steps[0].Command.Arguments.Should().Equal("-version");
        plan.Steps[1].Command.Arguments.Should().Contain("generate");
        plan.Steps[2].Command.Arguments.Should().Contain("archive");
        plan.Steps[3].Command.Arguments.Should().Contain("-exportArchive");
    }

    [Fact]
    public void EveryIosStepIsNamedForTheLog()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        // ⚠️ Not cosmetic. xcodebuild's failure is "exit code 65" whichever of
        // its two invocations produced it, so without the names a customer
        // cannot tell an archive that would not compile from an export that
        // would not sign.
        plan.Steps.Select(step => step.Name).Should().OnlyHaveUniqueItems();
        plan.Steps.Should().OnlyContain(step => step.Name.Length > 0);
    }

    [Fact]
    public void TheExportReadsThePlistThePlanWrites()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        var export = plan.Steps.Last().Command.Arguments;
        var plistArgument = export[export.ToList().IndexOf("-exportOptionsPlist") + 1];

        var written = plan.Files.Should().ContainSingle().Subject;

        // ⚠️ The one relationship in the plan that nothing else would catch. An
        // export pointed at a plist that was never written fails after the
        // archive, which is after all of the cost.
        plistArgument.Should().Be(Path.Combine(Lease.WorkspaceRoot, written.RelativePath));
    }

    [Fact]
    public void TheExportPlistNamesTheConfiguredTeamAndManualSigning()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        var plist = plan.Files.Single().Contents;

        plist.Should().Contain(TeamId);
        plist.Should().Contain("<string>manual</string>");
        plist.Should().Contain("<string>development</string>");
    }

    [Fact]
    public void TheArchiveNamesTheReleaseConfigurationAndRefusesProvisioningUpdates()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        var archive = plan.Steps[2].Command.Arguments.ToList();

        archive[archive.IndexOf("-configuration") + 1].Should().Be("Release");
        archive[archive.IndexOf("-scheme") + 1].Should().Be(BuildPlanner.IosScheme);

        // Every xcodebuild invocation, not just the archive: an export can mint
        // credentials just as readily.
        foreach (var step in plan.Steps.Where(step => step.Command.Executable == "xcodebuild"
            && step.Command.Arguments.Contains("-allowProvisioningUpdates")))
        {
            var arguments = step.Command.Arguments.ToList();
            arguments[arguments.IndexOf("-allowProvisioningUpdates") + 1].Should().Be("NO");
        }
    }

    [Fact]
    public void DerivedDataLivesUnderTheAppCacheRatherThanTheWorkspace()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        var archive = plan.Steps[2].Command.Arguments.ToList();
        var derivedData = archive[archive.IndexOf("-derivedDataPath") + 1];

        // ⚠️ Under the cache root, which is per app. DerivedData is writable
        // and expensive to rebuild; sharing it across tenants would be a place
        // for one customer's build to leave something another's links against,
        // and putting it in the workspace would throw it away every build.
        derivedData.Should().StartWith(Lease.CacheRoot);
        derivedData.Should().NotStartWith(Lease.WorkspaceRoot);
    }

    [Fact]
    public void ANamedDeveloperDirectoryReachesEveryXcodeInvocation()
    {
        var developerDirectory = "/Applications/Xcode-16.2.app/Contents/Developer";

        var plan = Planner(options => options.DeveloperDirectory = developerDirectory)
            .Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        // Every step, not the archive alone. An export run under a different
        // Xcode from the archive is the migration failure this exists to stop.
        plan.Steps.Should().OnlyContain(
            step => step.Command.Environment!["DEVELOPER_DIR"] == developerDirectory);
    }

    [Fact]
    public void NoDeveloperDirectoryLeavesTheHostDefault()
    {
        var plan = Planner().Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        plan.Steps.Should().OnlyContain(step => !step.Command.Environment!.ContainsKey("DEVELOPER_DIR"));
    }

    [Fact]
    public void AnIosBuildWithNoAppleTeamIsRefusedBeforeItStarts()
    {
        var planner = Planner(options => options.TeamId = null);

        var act = () => planner.Plan(Request(BuildPlatform.Ios, BuildType.Release), Lease, Project);

        // ⚠️ At planning time. xcodebuild finds this out only after the
        // archive, so refusing later would cost the customer the whole build
        // before telling them about a setting they cannot see.
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Apple team*");
    }

    [Fact]
    public void AnUnknownPlatformHasNoPlan()
    {
        var act = () => Planner().Plan(Request((BuildPlatform)99, BuildType.Release), Lease, Project);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void TheSchemeMatchesTheTargetTheShellTemplateDeclares()
    {
        var template = File.ReadAllLines(
            Path.Combine(RepositoryRoot(), "shells", "ios", "templates", "project.yml.tmpl"));

        var targetsAt = Array.FindIndex(template, line => line.StartsWith("targets:", StringComparison.Ordinal));
        targetsAt.Should().BeGreaterThan(-1, "the iOS shell template must declare targets");

        var target = template[targetsAt + 1].Trim().TrimEnd(':');

        // ⚠️ Read from the template rather than repeated here. XcodeGen names
        // the scheme after the target, so a rename in the shell that this
        // constant did not follow is a build that fails at the archive with
        // "scheme not found" and nothing to explain it.
        target.Should().Be(BuildPlanner.IosScheme);
    }

    [Theory]
    [InlineData("../escape.plist")]
    [InlineData("build/../../escape.plist")]
    [InlineData("/etc/passwd")]
    public void APlannedFileCannotEscapeTheWorkspace(string path)
    {
        var act = () => new PlannedFile(path, "contents");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab12cd34ef")]
    [InlineData("AB12CD34E")]
    [InlineData("AB12CD34EFG")]
    [InlineData("AB12</string><key>signingStyle</key><string>automatic")]
    public void AnExportPlistRefusesAnythingThatIsNotATeamIdentifier(string teamId)
    {
        var act = () => IosBuildCommands.ExportOptions(IosExportMethod.Development, teamId);

        // ⚠️ The value is interpolated into XML that decides how a binary is
        // signed. The last case is what a plist that parses as something other
        // than what it reads as would look like.
        act.Should().Throw<ArgumentException>();
    }

    private static BuildRequest Request(BuildPlatform platform, BuildType type) => new(
        BuildId: Guid.NewGuid(),
        OrgId: Guid.NewGuid(),
        AppId: Guid.NewGuid(),
        ConfigVersionId: Guid.NewGuid(),
        Platform: platform,
        Type: type);

    private static BuildPlanner Planner(Action<IosBuildOptions>? configure = null)
    {
        var options = new IosBuildOptions { TeamId = TeamId };
        configure?.Invoke(options);

        return new BuildPlanner(Options.Create(options));
    }

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test assembly.");
    }
}

/// <summary>
/// TC-S08-BLD-047–053 — the toolchain each platform's cache key names.
/// </summary>
/// <remarks>
/// ⚠️ These are the tests for the bug this sprint found. The orchestrator
/// computed every cache key against a single hash context that named no
/// toolchain at all, which meant a bump to AGP, Kotlin or Xcode changed no key
/// — so every app would have gone on being handed artifacts compiled by the
/// previous toolchain until something else in its config happened to change.
/// ADR 0004 exists to prevent exactly that.
/// </remarks>
public sealed class BuildToolchainsTests
{
    private static readonly JsonObjectFixture Config = new();

    [Fact]
    public void AndroidKeysAreComputedAgainstThePinnedAndroidToolchain()
    {
        Toolchains().HashContextFor(BuildPlatform.Android)
            .Should().Be(ToolchainDescriptor.Android.ToHashContext());
    }

    [Fact]
    public void IosKeysNameTheXcodeTheFleetActuallyRuns()
    {
        var context = Toolchains(options => options.XcodeVersion = "26.1")
            .HashContextFor(BuildPlatform.Ios);

        // Not the pinned default: the fleet builds with whatever is configured,
        // and a key that claimed otherwise would hand out binaries from a
        // toolchain nobody asked for.
        context.Toolchain!["xcode"].Should().Be("26.1");
        ToolchainDescriptor.Ios.Versions["xcode"].Should().NotBe("26.1");
    }

    [Fact]
    public void TheDefaultXcodeIsTheOneTheShellIsPinnedTo()
    {
        Toolchains().HashContextFor(BuildPlatform.Ios)
            .Toolchain!["xcode"].Should().Be(ToolchainDescriptor.Ios.Versions["xcode"]);
    }

    [Fact]
    public void BumpingXcodeInvalidatesEveryIosCodeKey()
    {
        var before = ConfigHasher.Compute(Config.Resolved, Toolchains().HashContextFor(BuildPlatform.Ios));
        var after = ConfigHasher.Compute(
            Config.Resolved,
            Toolchains(options => options.XcodeVersion = "26.1").HashContextFor(BuildPlatform.Ios));

        before.CodeKey.Should().NotBe(after.CodeKey);
    }

    [Fact]
    public void BumpingXcodeLeavesAndroidKeysAlone()
    {
        var before = ConfigHasher.Compute(Config.Resolved, Toolchains().HashContextFor(BuildPlatform.Android));
        var after = ConfigHasher.Compute(
            Config.Resolved,
            Toolchains(options => options.XcodeVersion = "26.1").HashContextFor(BuildPlatform.Android));

        // Android does not build with Xcode, so an Xcode bump must not throw
        // away every cached Android artifact.
        before.CodeKey.Should().Be(after.CodeKey);
    }

    [Fact]
    public void TheTwoPlatformsDoNotShareACodeKey()
    {
        var android = ConfigHasher.Compute(Config.Resolved, Toolchains().HashContextFor(BuildPlatform.Android));
        var ios = ConfigHasher.Compute(Config.Resolved, Toolchains().HashContextFor(BuildPlatform.Ios));

        android.CodeKey.Should().NotBe(ios.CodeKey);
    }

    [Fact]
    public void ContentKeysAreTheSameOnBothPlatforms()
    {
        var android = ConfigHasher.Compute(Config.Resolved, Toolchains().HashContextFor(BuildPlatform.Android));
        var ios = ConfigHasher.Compute(Config.Resolved, Toolchains().HashContextFor(BuildPlatform.Ios));

        // ⚠️ The content key is a projection of the document alone, by design.
        // A start URL is the same string on both platforms, and a content key
        // that varied by toolchain would make the patch fast path — the whole
        // point of splitting the key — impossible to hit after any bump.
        android.ContentKey.Should().Be(ios.ContentKey);
    }

    [Fact]
    public void AnUnknownPlatformHasNoPinnedToolchain()
    {
        var act = () => Toolchains().For((BuildPlatform)99);

        act.Should().Throw<NotSupportedException>();
    }

    private static BuildToolchains Toolchains(Action<IosBuildOptions>? configure = null)
    {
        var options = new IosBuildOptions();
        configure?.Invoke(options);

        return new BuildToolchains(Options.Create(options));
    }
}

/// <summary>A minimal resolved configuration, for hashing.</summary>
internal sealed class JsonObjectFixture
{
    /// <summary>The resolved document.</summary>
    public System.Text.Json.Nodes.JsonObject Resolved { get; } =
        (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(
            """
            {
              "app": {
                "name": "Acme",
                "bundleId": "com.acme.app",
                "versionName": "1.0.0",
                "versionCode": 1,
                "initialUrl": "https://acme.example"
              },
              "nativeSurfaces": []
            }
            """)!;
}
