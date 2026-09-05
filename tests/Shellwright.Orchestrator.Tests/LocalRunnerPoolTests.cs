using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shellwright.Orchestrator.Runner;
using Shellwright.Orchestrator.Workflows;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-BLD-057–064 — slots are bounded, leases expire, and workspaces go
/// with them.
/// </summary>
/// <remarks>
/// ⚠️ Time is injected, not slept through. A test that waits out a two-minute
/// lease is a test nobody runs, and one that shortens the lease to fifty
/// milliseconds tests a configuration that will never be deployed.
/// </remarks>
public sealed class LocalRunnerPoolTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"shellwright-pool-{Guid.NewGuid():N}");

    private readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-09-02T09:00:00Z", null));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "The pool hands out exactly as many slots as it has")]
    public async Task BoundedBySlots()
    {
        var pool = Pool(slots: 2);

        var first = await pool.TryLeaseAsync(Request());
        var second = await pool.TryLeaseAsync(Request());
        var third = await pool.TryLeaseAsync(Request());

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        // ⚠️ Null, not a wait. The activity turns this into a retryable failure
        // and Temporal backs off, which is the queue we would otherwise have
        // had to build — and it means a full fleet does not hold a worker
        // thread per waiting build.
        third.Should().BeNull();
    }

    [Fact(DisplayName = "A released slot is handed to the next build")]
    public async Task ReleaseFreesTheSlot()
    {
        var pool = Pool(slots: 1);

        var held = await pool.TryLeaseAsync(Request());
        (await pool.TryLeaseAsync(Request())).Should().BeNull();

        await pool.ReleaseAsync(held!);

        (await pool.TryLeaseAsync(Request())).Should().NotBeNull();
    }

    [Fact(DisplayName = "Releasing twice is not an error")]
    public async Task ReleaseIsIdempotent()
    {
        var pool = Pool(slots: 1);
        var held = await pool.TryLeaseAsync(Request());

        await pool.ReleaseAsync(held!);

        // Release runs on the compensation path, which Temporal may run more
        // than once. A second release that threw would fail an already-failed
        // build for a second reason.
        var act = () => pool.ReleaseAsync(held!);
        await act.Should().NotThrowAsync();
    }

    [Fact(DisplayName = "A lease nobody renews expires and its slot comes back")]
    public async Task ExpiredLeasesAreReclaimed()
    {
        var pool = Pool(slots: 1, ttl: TimeSpan.FromMinutes(2));

        await pool.TryLeaseAsync(Request());
        (await pool.TryLeaseAsync(Request())).Should().BeNull();

        // The worker holding it died. Nothing will ever release it.
        clock.Advance(TimeSpan.FromMinutes(3));

        (await pool.TryLeaseAsync(Request())).Should().NotBeNull();
    }

    [Fact(DisplayName = "Heartbeating keeps a long build's lease alive")]
    public async Task RenewalHoldsTheSlot()
    {
        var pool = Pool(slots: 1, ttl: TimeSpan.FromMinutes(2));
        var held = await pool.TryLeaseAsync(Request());

        // A four-minute build that heartbeats every ninety seconds.
        for (var beat = 0; beat < 3; beat++)
        {
            clock.Advance(TimeSpan.FromSeconds(90));
            await pool.RenewAsync(held!);
        }

        pool.InUse.Should().Be(1);
        (await pool.TryLeaseAsync(Request())).Should().BeNull();
    }

    [Fact(DisplayName = "Renewing a lease that already expired is refused loudly")]
    public async Task RenewingAnExpiredLeaseThrows()
    {
        var pool = Pool(slots: 1, ttl: TimeSpan.FromMinutes(2));
        var held = await pool.TryLeaseAsync(Request());

        clock.Advance(TimeSpan.FromMinutes(3));

        // Force the reclaim that a lease attempt would also have done.
        pool.InUse.Should().Be(0);

        // ⚠️ Loud, because the slot may already belong to another build — so
        // this worker is about to write into a workspace it no longer owns.
        var act = () => pool.RenewAsync(held!);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "Reclaiming an expired lease destroys its workspace")]
    public async Task ReclaimDestroysTheWorkspace()
    {
        var pool = Pool(slots: 1, ttl: TimeSpan.FromMinutes(2));
        var held = await pool.TryLeaseAsync(Request());

        Directory.CreateDirectory(held!.WorkspaceRoot);
        await File.WriteAllTextAsync(
            Path.Combine(held.WorkspaceRoot, "MainActivity.kt"),
            "a customer's source");

        clock.Advance(TimeSpan.FromMinutes(3));

        var next = await pool.TryLeaseAsync(Request());

        // ⚠️ The slot and the disk are freed together. A lease expires because
        // the worker died, so nothing is ever going to call ReleaseRunnerAsync
        // for it — and handing the slot on while the previous tenant's source
        // is still there is the exact failure isolation exists to prevent.
        Directory.Exists(held.WorkspaceRoot).Should().BeFalse();
        next.Should().NotBeNull();
        next!.WorkspaceRoot.Should().NotBe(held.WorkspaceRoot);
    }

    [Fact(DisplayName = "Two builds of the same app share a dependency cache but never a workspace")]
    public async Task CachesAreSharedPerAppAndWorkspacesNever()
    {
        var pool = Pool(slots: 4);
        var app = Guid.NewGuid();
        var other = Guid.NewGuid();

        var first = await pool.TryLeaseAsync(Request(app));
        var second = await pool.TryLeaseAsync(Request(app));
        var third = await pool.TryLeaseAsync(Request(other));

        // Shared, because being reused is the whole point of a dependency cache.
        first!.CacheRoot.Should().Be(second!.CacheRoot);

        // ⚠️ Never shared across apps. A Gradle cache is a directory a build can
        // write to, so one shared between tenants lets a customer's build plant
        // a jar another customer's build compiles against.
        third!.CacheRoot.Should().NotBe(first.CacheRoot);

        // ⚠️ And never shared at all, even within one app — a retried build must
        // get a workspace with nothing in it.
        first.WorkspaceRoot.Should().NotBe(second.WorkspaceRoot);
    }

    /// <summary>
    /// A build request, with every field named.
    /// </summary>
    /// <remarks>
    /// ⚠️ Named arguments, because BuildRequest opens with four bare
    /// <see cref="Guid"/> fields and putting the app id in the org id's slot
    /// compiles perfectly and fails a test somewhere else entirely.
    /// </remarks>
    private static BuildRequest Request(Guid? appId = null) =>
        new(
            BuildId: Guid.NewGuid(),
            OrgId: Guid.NewGuid(),
            AppId: appId ?? Guid.NewGuid(),
            ConfigVersionId: Guid.NewGuid(),
            Platform: BuildPlatform.Android,
            Type: BuildType.Debug);

    private LocalRunnerPool Pool(int slots, TimeSpan? ttl = null) =>
        new(
            Options.Create(new RunnerPoolOptions
            {
                Slots = slots,
                WorkspaceRoot = Path.Combine(root, "workspaces"),
                CacheRoot = Path.Combine(root, "caches"),
                LeaseTimeToLive = ttl ?? TimeSpan.FromMinutes(2),
            }),
            clock,
            NullLogger<LocalRunnerPool>.Instance);
}
