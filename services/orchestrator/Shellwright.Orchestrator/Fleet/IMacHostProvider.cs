using System.Collections.Immutable;

namespace Shellwright.Orchestrator.Fleet;

/// <summary>
/// Where macOS hosts come from.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ This interface is the whole point of the fleet code, and it exists
/// because of a migration the plan already commits to: start on a hosted Mac
/// provider for the alpha, move to owned Apple Silicon minis with Tart once
/// volume justifies the capex. The risk register calls for a "provider
/// abstraction; hosted → owned migration path", and this is it — the move
/// should be one implementation, not a refactor of everything that touches a
/// build.
/// </para>
/// <para>
/// ⚠️ Deliberately narrow. It reports hosts and their health, and it does not
/// place builds: <see cref="MacFleet"/> does that, with no I/O, because
/// Apple's licence cap and the N+1 reserve are rules that must hold whoever
/// supplies the hardware. A provider that could place its own builds would be
/// a provider that could quietly break them.
/// </para>
/// <para>
/// ⚠️ No implementation here is a real one. This project has no Mac and no
/// provider account, so what ships is the seam and the rules — recorded as a
/// gap in <c>ACTION_REQUIRED.md</c> rather than dressed up as a fleet.
/// </para>
/// </remarks>
public interface IMacHostProvider
{
    /// <summary>Who this is, for logs and metrics.</summary>
    string Name { get; }

    /// <summary>Lists the hosts this provider currently offers.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The hosts, with their current state.</returns>
    Task<ImmutableArray<MacHost>> ListHostsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepares a host to run one build, and returns where to run it.
    /// </summary>
    /// <param name="host">The host <see cref="MacFleet"/> chose.</param>
    /// <param name="xcodeVersion">Which Xcode the build asked for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How to reach the prepared VM.</returns>
    /// <remarks>
    /// ⚠️ On an owned fleet this restores a golden VM snapshot, which the spec
    /// costs at 2–10 seconds against minutes for cleaning a dirty machine. A
    /// hosted provider may do something else entirely. What both must
    /// guarantee is the same thing the Linux sandbox guarantees: the build
    /// starts on a machine holding nothing from the previous tenant.
    /// </remarks>
    Task<MacWorkspace> PrepareAsync(
        MacHost host,
        string xcodeVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a host to the pool, destroying what the build left.</summary>
    /// <param name="workspace">What <see cref="PrepareAsync"/> returned.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once nothing of the build remains.</returns>
    Task ReleaseAsync(MacWorkspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Asks whether a host is fit to take work.</summary>
    /// <param name="host">The host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether it passed.</returns>
    /// <remarks>
    /// ⚠️ Must actually exercise the toolchain rather than ping the host. The
    /// spec's warning is that "Xcode installs corrupt more often than you'd
    /// think", and a Mac that answers SSH while its Xcode is broken will take
    /// every build in the queue and fail all of them.
    /// </remarks>
    Task<bool> CheckHealthAsync(MacHost host, CancellationToken cancellationToken = default);
}

/// <summary>A prepared macOS VM, ready to build in.</summary>
/// <param name="HostId">Which host it is on.</param>
/// <param name="WorkspaceId">Identifies this VM, for release.</param>
/// <param name="XcodeVersion">Which Xcode it has.</param>
/// <param name="WorkspaceRoot">Where the build's files go.</param>
/// <param name="DerivedDataRoot">
/// Where Xcode's intermediates go. ⚠️ Per app, never shared: DerivedData is
/// writable, and one shared between tenants is a way for one customer's build
/// to leave something another customer's build links against.
/// </param>
public sealed record MacWorkspace(
    string HostId,
    string WorkspaceId,
    string XcodeVersion,
    string WorkspaceRoot,
    string DerivedDataRoot);
