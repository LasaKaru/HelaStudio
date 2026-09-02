using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shellwright.Api.Data;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Builds;

/// <summary>Build API settings.</summary>
public sealed class BuildOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Builds";

    /// <summary>
    /// How many builds one organisation may have running at once.
    /// </summary>
    /// <remarks>
    /// ⚠️ Per organisation rather than global, because the failure this
    /// prevents is one customer's CI loop consuming the whole fleet. A global
    /// cap would let that happen and only tell everybody else that the service
    /// is slow.
    ///
    /// Two on the free plan: enough that Android and iOS can run together,
    /// few enough that a misconfigured pipeline cannot queue a hundred.
    /// </remarks>
    [Range(1, 100)]
    public int MaxConcurrentBuildsPerOrg { get; set; } = 2;
}

/// <summary>Everything needed to start one build.</summary>
/// <param name="AppId">The app.</param>
/// <param name="OrgId">Who is charged.</param>
/// <param name="ConfigVersionId">Exactly what to build.</param>
/// <param name="Platform">Which platform.</param>
/// <param name="Type">Debug or release.</param>
/// <param name="RequestedBy">Who asked, or null for a token with no user.</param>
/// <param name="IdempotencyKey">The key the caller sent.</param>
public sealed record BuildLaunch(
    Guid AppId,
    Guid OrgId,
    Guid ConfigVersionId,
    BuildPlatform Platform,
    BuildType Type,
    Guid? RequestedBy,
    string IdempotencyKey);

/// <summary>What starting a build produced.</summary>
/// <param name="Started">The new build, when one was created.</param>
/// <param name="Existing">
/// The build this request already started, when the same idempotency key came
/// back.
/// </param>
/// <param name="ConcurrencyExceeded">True when the organisation is at its limit.</param>
public sealed record BuildLaunchOutcome(Build? Started, Build? Existing, bool ConcurrencyExceeded);

/// <summary>Starts and cancels builds.</summary>
public interface IBuildWorkflowClient
{
    /// <summary>Starts the workflow that runs a build.</summary>
    /// <param name="build">The build row that was created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the workflow has been accepted.</returns>
    Task StartAsync(Build build, CancellationToken cancellationToken = default);

    /// <summary>Asks a running workflow to stop.</summary>
    /// <param name="workflowId">Which workflow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the request has been delivered.</returns>
    Task CancelAsync(string workflowId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates the build row and starts the workflow that runs it.
/// </summary>
/// <remarks>
/// ⚠️ The row is written first and the workflow started second, and the order
/// is not arbitrary. A workflow with no row is a build nobody can see, cancel or
/// bill; a row with no workflow is a build stuck in Queued, which is visible,
/// diagnosable and recoverable. Of the two ways this can half-fail, only one
/// leaves evidence.
/// </remarks>
/// <param name="database">The database context.</param>
/// <param name="workflows">Starts and cancels workflows.</param>
/// <param name="options">Build settings.</param>
public sealed class BuildLauncher(
    ShellwrightDbContext database,
    IBuildWorkflowClient workflows,
    IOptions<BuildOptions> options)
{
    private static readonly BuildState[] Running =
    [
        BuildState.Queued,
        BuildState.Generating,
        BuildState.Building,
        BuildState.Verifying,
    ];

    private readonly BuildOptions settings = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Starts a build, or returns the one this request already started.</summary>
    /// <param name="launch">What to build.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened.</returns>
    public async Task<BuildLaunchOutcome> StartAsync(
        BuildLaunch launch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(launch);

        // A fast path, not the guarantee. The unique index below is what makes
        // this correct under a retry that races itself; this only saves the
        // common case from producing an exception.
        if (await FindByKeyAsync(launch, cancellationToken) is { } already)
        {
            return new BuildLaunchOutcome(null, already, false);
        }

        var running = await database.Builds
            .CountAsync(x => x.OrgId == launch.OrgId && Running.Contains(x.State), cancellationToken);

        if (running >= settings.MaxConcurrentBuildsPerOrg)
        {
            return new BuildLaunchOutcome(null, null, true);
        }

        var buildId = Guid.CreateVersion7();

        var build = new Build
        {
            Id = buildId,
            AppId = launch.AppId,
            OrgId = launch.OrgId,
            ConfigVersionId = launch.ConfigVersionId,
            Platform = launch.Platform,
            Type = launch.Type,
            State = BuildState.Queued,
            RequestedBy = launch.RequestedBy,
            IdempotencyKey = launch.IdempotencyKey,

            // ⚠️ Derived from the build id rather than generated separately, so
            // anyone holding either identifier can find the other — which is
            // what makes a stuck workflow diagnosable from a build row, and a
            // stray workflow traceable back to a tenant. A second source of
            // randomness would be one more thing that can drift out of step.
            WorkflowId = WorkflowIdFor(buildId),
        };

        database.Builds.Add(build);

        // The first transition is recorded with the build, so a build's history
        // starts at the moment it was accepted rather than at the moment a
        // worker first touched it.
        database.BuildTransitions.Add(new BuildTransition
        {
            BuildId = buildId,
            State = BuildState.Queued,
        });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // ⚠️ The retry that raced itself. Both requests found nothing on
            // the read above; the index let exactly one of them win. The loser
            // reports the winner's build, which is what the caller wanted.
            database.ChangeTracker.Clear();

            var winner = await FindByKeyAsync(launch, cancellationToken);

            return winner is null
                ? throw new InvalidOperationException(
                    "A build insert hit a unique violation, but no build carries that idempotency key. "
                    + "Some other constraint on builds is being violated.")
                : new BuildLaunchOutcome(null, winner, false);
        }

        await workflows.StartAsync(build, cancellationToken);

        return new BuildLaunchOutcome(build, null, false);
    }

    /// <summary>Asks a running build to stop.</summary>
    /// <param name="workflowId">Which workflow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the request has been delivered.</returns>
    public Task CancelAsync(string workflowId, CancellationToken cancellationToken = default) =>
        workflows.CancelAsync(workflowId, cancellationToken);

    /// <summary>The workflow id for a build.</summary>
    /// <param name="buildId">The build.</param>
    /// <returns>The identifier Temporal knows it by.</returns>
    public static string WorkflowIdFor(Guid buildId) =>
        string.Create(CultureInfo.InvariantCulture, $"build-{buildId}");

    private async Task<Build?> FindByKeyAsync(BuildLaunch launch, CancellationToken cancellationToken) =>
        await database.Builds
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.AppId == launch.AppId && x.IdempotencyKey == launch.IdempotencyKey,
                cancellationToken);
}
