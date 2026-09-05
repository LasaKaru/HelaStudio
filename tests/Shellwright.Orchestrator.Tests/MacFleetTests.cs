using System.Collections.Immutable;
using FluentAssertions;
using Shellwright.Orchestrator.Fleet;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S08-BLD-001–012 — where an iOS build is allowed to run.
/// </summary>
/// <remarks>
/// ⚠️ The macOS fleet is the one part of this system that cannot be tested
/// against the real thing here — there is no Mac and no provider account. What
/// can be tested is the part with the actual rules: Apple's two-VM licence cap,
/// N and N−1 Xcode, the N+1 spare host, and draining rather than killing. That
/// logic is deliberately free of I/O so it is testable at all, and these are
/// the tests that make keeping it that way worthwhile.
/// </remarks>
public sealed class MacFleetTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-03T09:00:00Z", null);

    [Fact(DisplayName = "Apple's two-VM licence cap is respected even when the hardware could do more")]
    public void RespectsTheLicenceCap()
    {
        // A Mac mini with 48 GB could run four of these. It may not.
        var fleet = new[] { Host("mac-1", active: 2), Host("mac-2", active: 2) };

        var decision = MacFleet.Place(fleet, "26.1", keepReserve: false);

        decision.Granted.Should().BeFalse();
        decision.Refusal.Should().Be(FleetRefusal.AtCapacity);
        MacFleet.MaxVirtualMachinesPerHost.Should().Be(2, "this is a licence term, not a tuning knob");
    }

    [Fact(DisplayName = "A build asking for an Xcode nobody has is refused by name")]
    public void RefusesAnUnknownXcode()
    {
        var fleet = new[] { Host("mac-1", xcode: ["26.1", "26.0"]) };

        var decision = MacFleet.Place(fleet, "27.0");

        // ⚠️ Its own refusal rather than "at capacity". Apple's submission
        // deadlines force fleet-wide Xcode migrations, and during one this is
        // the difference between "we have not rolled that out yet" and "buy
        // more Macs".
        decision.Refusal.Should().Be(FleetRefusal.NoSuchXcode);
    }

    [Fact(DisplayName = "Both N and N−1 Xcode are placeable during a migration")]
    public void PlacesBothSupportedXcodeVersions()
    {
        // The spec's mitigation: run N and N−1 together, migrate on a schedule
        // with a canary host, rather than moving the fleet in one step.
        var fleet = new[]
        {
            Host("mac-old", xcode: ["26.0"]),
            Host("mac-canary", xcode: ["26.1", "26.0"]),
            Host("mac-spare", xcode: ["26.1", "26.0"]),
        };

        MacFleet.Place(fleet, "26.0").Granted.Should().BeTrue();
        MacFleet.Place(fleet, "26.1").Granted.Should().BeTrue();
    }

    [Fact(DisplayName = "A draining host finishes its work and takes none")]
    public void DrainingHostsTakeNoNewWork()
    {
        var fleet = new[]
        {
            Host("mac-1", state: HostState.Draining, active: 1),
            Host("mac-2", active: 0),
            Host("mac-3", active: 0),
        };

        var decision = MacFleet.Place(fleet, "26.1");

        decision.Host!.HostId.Should().NotBe("mac-1");

        // ⚠️ And it keeps the build it already has. Killing it to make the
        // point would throw away runner minutes a customer is paying for.
        fleet.Single(x => x.HostId == "mac-1").ActiveBuilds.Should().Be(1);
    }

    [Fact(DisplayName = "An unhealthy host is never chosen")]
    public void UnhealthyHostsAreNeverChosen()
    {
        var fleet = new[]
        {
            Host("mac-broken", state: HostState.Unhealthy, active: 0),
            Host("mac-ok", active: 1),
            Host("mac-spare", active: 0),
        };

        MacFleet.Place(fleet, "26.1").Host!.HostId.Should().Be("mac-ok");
    }

    [Fact(DisplayName = "The fleet packs onto busy hosts so whole hosts stay drainable")]
    public void PacksRatherThanSpreads()
    {
        var fleet = new[]
        {
            Host("mac-1", active: 1),
            Host("mac-2", active: 0),
            Host("mac-3", active: 0),
        };

        // ⚠️ Fullest-first. Spreading evenly keeps every host busy and none
        // ever drainable, which makes an Xcode migration need a maintenance
        // window instead of a rolling drain.
        MacFleet.Place(fleet, "26.1").Host!.HostId.Should().Be("mac-1");
    }

    [Fact(DisplayName = "Packing is what keeps the spare intact")]
    public void PackingPreservesTheSpare()
    {
        // The two behaviours are the same behaviour: placing onto the host that
        // is already working is what leaves mac-2 wholly idle and therefore
        // drainable, so the reserve costs nothing until the fleet is genuinely
        // full.
        var fleet = new[] { Host("mac-1", active: 1), Host("mac-2", active: 0) };

        var decision = MacFleet.Place(fleet, "26.1", keepReserve: true);

        decision.Host!.HostId.Should().Be("mac-1");
        fleet.Single(x => x.HostId == "mac-2").ActiveBuilds.Should().Be(0);
    }

    [Fact(DisplayName = "The last idle host is held back as the N+1 spare")]
    public void HoldsBackTheSpare()
    {
        // ⚠️ mac-1 is *full*, so the only placement left is the idle host —
        // which is the spare. An earlier version of this test used a host with
        // one build free, and packing correctly placed there and left mac-2
        // idle: the reserve was preserved, and the test was wrong rather than
        // the rule.
        var fleet = new[] { Host("mac-1", active: 2), Host("mac-2", active: 0) };

        var decision = MacFleet.Place(fleet, "26.1", keepReserve: true);

        decision.Granted.Should().BeFalse();

        // ⚠️ Reported as a reserve, not as capacity exhaustion. The fleet has
        // room and is deliberately not using it; calling that "at capacity"
        // would get the reserve optimised away by whoever reads the metric.
        decision.Refusal.Should().Be(FleetRefusal.ReserveOnly);
    }

    [Fact(DisplayName = "The spare can be spent when the caller says so")]
    public void TheSpareCanBeSpent()
    {
        var fleet = new[] { Host("mac-1", active: 2), Host("mac-2", active: 0) };

        MacFleet.Place(fleet, "26.1", keepReserve: false).Granted.Should().BeTrue();
    }

    [Fact(DisplayName = "A single-host fleet still builds")]
    public void ASingleHostFleetStillBuilds()
    {
        // ⚠️ The alpha runs on one hosted Mac. A reserve rule that refused
        // every build on a one-host fleet would be correct in principle and
        // useless in practice.
        var fleet = new[] { Host("mac-only", active: 0) };

        MacFleet.Place(fleet, "26.1", keepReserve: true).Granted.Should().BeTrue();
    }

    [Fact(DisplayName = "A host that stops checking in is drained, not killed")]
    public void StaleHostsAreDrained()
    {
        var fleet = new[]
        {
            Host("mac-quiet", active: 1) with { LastHealthyAt = Now.AddMinutes(-30) },
            Host("mac-fine", active: 0) with { LastHealthyAt = Now.AddSeconds(-10) },
        };

        var after = MacFleet.ApplyHealth(fleet, Now, TimeSpan.FromMinutes(5));

        // ⚠️ Draining rather than Unhealthy: a missed check is far more often a
        // network blip than a dead Mac, and the build in flight may be fine.
        after.Single(x => x.HostId == "mac-quiet").State.Should().Be(HostState.Draining);
        after.Single(x => x.HostId == "mac-fine").State.Should().Be(HostState.Healthy);
    }

    [Fact(DisplayName = "Health checks do not resurrect a host somebody drained")]
    public void HealthDoesNotUndoADeliberateDrain()
    {
        var fleet = new[]
        {
            Host("mac-1", state: HostState.Draining) with { LastHealthyAt = Now },
            Host("mac-2", state: HostState.Reserve) with { LastHealthyAt = Now },
        };

        var after = MacFleet.ApplyHealth(fleet, Now, TimeSpan.FromMinutes(5));

        // Somebody draining a host for an Xcode upgrade must not have it
        // un-drained by the next health tick.
        after.Single(x => x.HostId == "mac-1").State.Should().Be(HostState.Draining);
        after.Single(x => x.HostId == "mac-2").State.Should().Be(HostState.Reserve);
    }

    [Fact(DisplayName = "Capacity counts free slots on healthy hosts only")]
    public void CapacityCountsWhatCanActuallyRun()
    {
        var fleet = new[]
        {
            Host("mac-1", active: 1),
            Host("mac-2", active: 0),
            Host("mac-draining", state: HostState.Draining, active: 0),
            Host("mac-broken", state: HostState.Unhealthy, active: 0),
        };

        // One free slot on mac-1, two on mac-2. The draining and unhealthy
        // hosts contribute nothing, however idle they look.
        MacFleet.AvailableCapacity(fleet).Should().Be(3);
    }

    private static MacHost Host(
        string id,
        HostState state = HostState.Healthy,
        int active = 0,
        string[]? xcode = null) =>
        new(
            id,
            "test-provider",
            [.. xcode ?? ["26.1", "26.0"]],
            state,
            active,
            Now);
}
