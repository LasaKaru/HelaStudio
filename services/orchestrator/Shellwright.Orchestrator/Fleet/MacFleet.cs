using System.Collections.Immutable;

namespace Shellwright.Orchestrator.Fleet;

/// <summary>What a macOS host is currently good for.</summary>
public enum HostState
{
    /// <summary>Taking work.</summary>
    Healthy = 0,

    /// <summary>
    /// Finishing what it has and taking nothing new.
    /// </summary>
    /// <remarks>
    /// ⚠️ Distinct from <see cref="Unhealthy"/> on purpose. A host being
    /// migrated to a new Xcode is perfectly well; killing the builds already
    /// running on it to make that point would throw away minutes a customer is
    /// paying for.
    /// </remarks>
    Draining = 1,

    /// <summary>Failed its health check. Takes nothing, and what it holds is suspect.</summary>
    Unhealthy = 2,

    /// <summary>Deliberately held out of service as spare capacity.</summary>
    Reserve = 3,
}

/// <summary>One macOS host in the fleet.</summary>
/// <param name="HostId">Stable identifier.</param>
/// <param name="Provider">Who runs the hardware.</param>
/// <param name="XcodeVersions">
/// Which Xcode versions this host can build with, newest first.
/// </param>
/// <param name="State">What it is currently good for.</param>
/// <param name="ActiveBuilds">How many VMs are busy.</param>
/// <param name="LastHealthyAt">When it last passed a health check.</param>
public sealed record MacHost(
    string HostId,
    string Provider,
    ImmutableArray<string> XcodeVersions,
    HostState State,
    int ActiveBuilds,
    DateTimeOffset LastHealthyAt);

/// <summary>Why a lease could not be granted.</summary>
public enum FleetRefusal
{
    /// <summary>A slot was granted.</summary>
    None = 0,

    /// <summary>No host carries the requested Xcode.</summary>
    NoSuchXcode = 1,

    /// <summary>Every host that could take it is busy.</summary>
    AtCapacity = 2,

    /// <summary>
    /// Granting would eat the spare host.
    /// </summary>
    /// <remarks>
    /// ⚠️ A distinct answer from <see cref="AtCapacity"/>, because it means
    /// something different to whoever is watching: the fleet has room and is
    /// deliberately not using it. Collapsing the two would make the reserve
    /// look like a capacity shortfall and get it "optimised" away.
    /// </remarks>
    ReserveOnly = 3,
}

/// <summary>What the fleet decided.</summary>
/// <param name="Host">The host to build on, when one was granted.</param>
/// <param name="Refusal">Why not, when one was not.</param>
public sealed record FleetDecision(MacHost? Host, FleetRefusal Refusal)
{
    /// <summary>Whether a host was granted.</summary>
    public bool Granted => Host is not null;

    /// <summary>A granted decision.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The decision.</returns>
    public static FleetDecision Grant(MacHost host) => new(host, FleetRefusal.None);

    /// <summary>A refusal.</summary>
    /// <param name="refusal">Why.</param>
    /// <returns>The decision.</returns>
    public static FleetDecision Refuse(FleetRefusal refusal) => new(null, refusal);
}

/// <summary>
/// Which macOS host a build should run on, and whether it may run at all.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Pure, and separate from anything that talks to a provider. Fleet
/// placement is the part of macOS operations with real rules — Apple's two-VM
/// licence cap, N and N−1 Xcode, an N+1 spare host, draining rather than
/// killing — and it is the part that cannot be tested against a provider this
/// project does not have an account with. Keeping it free of I/O is what makes
/// it testable at all.
/// </para>
/// <para>
/// ⚠️ The two-VM cap is Apple's macOS licence, not a performance tuning knob.
/// A host with 48 GB could run four builds and may not. Encoding it as a
/// constant with this comment attached is the difference between a rule
/// somebody can look up and a number somebody raises to clear a queue.
/// </para>
/// </remarks>
public static class MacFleet
{
    /// <summary>
    /// How many VMs Apple's macOS licence permits per physical host.
    /// </summary>
    /// <remarks>
    /// ⚠️ A licence term, not a capacity setting. Raising it is a licence
    /// violation regardless of what the hardware could do.
    /// </remarks>
    public const int MaxVirtualMachinesPerHost = 2;

    /// <summary>Chooses a host for a build, or explains why it cannot run.</summary>
    /// <param name="hosts">The fleet.</param>
    /// <param name="xcodeVersion">The Xcode version the build asked for.</param>
    /// <param name="keepReserve">
    /// Whether to hold one healthy host back. ⚠️ The spec's N+1: a spare that
    /// does nothing but wait, so a host failing mid-day does not mean a queue.
    /// </param>
    /// <returns>What the fleet decided.</returns>
    public static FleetDecision Place(
        IReadOnlyCollection<MacHost> hosts,
        string xcodeVersion,
        bool keepReserve = true)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentException.ThrowIfNullOrWhiteSpace(xcodeVersion);

        var capable = hosts
            .Where(host => host.XcodeVersions.Contains(xcodeVersion, StringComparer.Ordinal))
            .ToList();

        if (capable.Count == 0)
        {
            return FleetDecision.Refuse(FleetRefusal.NoSuchXcode);
        }

        // Draining and unhealthy hosts take nothing; Reserve is held back for
        // the check below rather than treated as available.
        var available = capable
            .Where(host => host.State == HostState.Healthy)
            .Where(host => host.ActiveBuilds < MaxVirtualMachinesPerHost)
            .ToList();

        if (available.Count == 0)
        {
            // ⚠️ Distinguish "the fleet is full" from "the fleet is holding a
            // spare". Only the second is a decision somebody made.
            var reserved = capable.Any(host =>
                host.State == HostState.Reserve && host.ActiveBuilds < MaxVirtualMachinesPerHost);

            return FleetDecision.Refuse(reserved ? FleetRefusal.ReserveOnly : FleetRefusal.AtCapacity);
        }

        if (keepReserve && WouldExhaustFleet(capable, available))
        {
            return FleetDecision.Refuse(FleetRefusal.ReserveOnly);
        }

        // ⚠️ Fullest-first, not emptiest-first. Packing builds onto hosts that
        // are already working leaves whole hosts idle and therefore drainable —
        // which is what makes an Xcode migration possible without a maintenance
        // window. Spreading load evenly would mean every host is always busy
        // and none can ever be taken out of service.
        var chosen = available
            .OrderByDescending(host => host.ActiveBuilds)
            .ThenBy(host => host.HostId, StringComparer.Ordinal)
            .First();

        return FleetDecision.Grant(chosen);
    }

    /// <summary>Marks hosts that have not checked in recently as unhealthy.</summary>
    /// <param name="hosts">The fleet.</param>
    /// <param name="now">Current time.</param>
    /// <param name="tolerance">How long a host may go without passing a check.</param>
    /// <returns>The fleet, with stale hosts drained.</returns>
    /// <remarks>
    /// ⚠️ Drained rather than marked unhealthy outright. A host that missed a
    /// check may still be finishing a build correctly, and a missed check is
    /// far more often a network blip than a dead Mac. Draining stops new work
    /// without throwing away work in progress; a host that stays silent will be
    /// noticed by whoever reads the drain.
    /// </remarks>
    public static ImmutableArray<MacHost> ApplyHealth(
        IReadOnlyCollection<MacHost> hosts,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        return [.. hosts.Select(host =>
            host.State == HostState.Healthy && now - host.LastHealthyAt > tolerance
                ? host with { State = HostState.Draining }
                : host)];
    }

    /// <summary>How many more builds the fleet could take right now.</summary>
    /// <param name="hosts">The fleet.</param>
    /// <returns>The number of free VM slots on healthy hosts.</returns>
    public static int AvailableCapacity(IReadOnlyCollection<MacHost> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);

        return hosts
            .Where(host => host.State == HostState.Healthy)
            .Sum(host => Math.Max(0, MaxVirtualMachinesPerHost - host.ActiveBuilds));
    }

    /// <summary>
    /// Whether granting one more build would leave no whole host spare.
    /// </summary>
    /// <remarks>
    /// The reserve is a whole idle host rather than a free slot: a host that is
    /// half busy cannot be snapshot-restored or taken out for an Xcode upgrade,
    /// so a spare slot is not a spare host.
    /// </remarks>
    private static bool WouldExhaustFleet(
        IReadOnlyCollection<MacHost> capable,
        IReadOnlyCollection<MacHost> available)
    {
        // A fleet of one cannot hold a spare and still do any work. Refusing
        // every build on a single-host alpha would be worse than useless.
        if (capable.Count(host => host.State is HostState.Healthy or HostState.Reserve) <= 1)
        {
            return false;
        }

        var idleHosts = available.Count(host => host.ActiveBuilds == 0);
        var wouldStayIdle = available.Any(host => host.ActiveBuilds > 0)
            ? idleHosts
            : idleHosts - 1;

        return wouldStayIdle < 1;
    }
}
