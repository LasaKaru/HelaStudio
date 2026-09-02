using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Workflows;
using Temporalio.Extensions.Hosting;

namespace Shellwright.Orchestrator.Hosting;

/// <summary>Orchestrator settings.</summary>
public sealed class OrchestratorOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Orchestrator";

    /// <summary>Temporal front-end address.</summary>
    [Required]
    public string TemporalAddress { get; set; } = "localhost:7233";

    /// <summary>Temporal namespace.</summary>
    [Required]
    public string TemporalNamespace { get; set; } = "default";

    /// <summary>
    /// How many builds this worker will run at once.
    /// </summary>
    /// <remarks>
    /// ⚠️ One, on the Oracle host, and not out of caution. The box is 2 OCPU and
    /// 12 GB shared with Postgres, Redis and Temporal itself; a single Gradle
    /// build is configured for 2 GB of heap and will use it. Two concurrent
    /// builds is how the database gets killed by the OOM killer during someone
    /// else's build.
    /// </remarks>
    [Range(1, 32)]
    public int MaxConcurrentBuilds { get; set; } = 1;
}

/// <summary>Registers the worker.</summary>
public static class OrchestratorHostExtensions
{
    /// <summary>Adds the Temporal worker, its workflows, and its activities.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightOrchestrator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OrchestratorOptions>()
            .Bind(configuration.GetSection(OrchestratorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(OrchestratorOptions.SectionName).Get<OrchestratorOptions>()
            ?? new OrchestratorOptions();

        services
            .AddHostedTemporalWorker(
                options.TemporalAddress,
                options.TemporalNamespace,
                BuildWorkflow.TaskQueue)
            .AddScopedActivities<BuildActivities>()
            .AddWorkflow<BuildWorkflow>();

        return services;
    }
}
