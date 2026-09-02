using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Logs;
using Shellwright.Orchestrator.Workflows;
using StackExchange.Redis;
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

        services.AddShellwrightBuildLogs(configuration);

        return services;
    }

    /// <summary>Adds the build log pipeline.</summary>
    /// <remarks>
    /// The reader is deliberately not registered here: the worker writes logs
    /// and the API reads them, and they run in different processes against the
    /// same Redis.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightBuildLogs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<LogPipelineOptions>()
            .Bind(configuration.GetSection(LogPipelineOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ⚠️ Connected lazily and never as a hard dependency of startup. A
        // worker that cannot reach Redis must still start and still build:
        // failing to boot because the live log stream is unavailable would take
        // the whole service down for a convenience feature.
        services.AddSingleton(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<LogPipelineOptions>>().Value;

            if (string.IsNullOrWhiteSpace(settings.RedisConnectionString))
            {
                return new LiveLogConnection(Connection: null);
            }

            var redisOptions = ConfigurationOptions.Parse(settings.RedisConnectionString);
            redisOptions.AbortOnConnectFail = false;

            try
            {
                return new LiveLogConnection(ConnectionMultiplexer.Connect(redisOptions));
            }
            catch (RedisConnectionException exception)
            {
                provider.GetRequiredService<ILogger<RedisLogPipeline>>().LogWarning(
                    exception,
                    "Could not reach Redis. Build logs will be archived but not streamed live.");

                return new LiveLogConnection(Connection: null);
            }
        });

        services.AddSingleton(provider => new RedisLogPipeline(
            provider.GetRequiredService<LiveLogConnection>().Connection,
            provider.GetRequiredService<IOptions<LogPipelineOptions>>(),
            provider.GetRequiredService<ILogger<RedisLogPipeline>>()));

        services.AddSingleton<IBuildLogPipeline>(
            provider => provider.GetRequiredService<RedisLogPipeline>());

        return services;
    }
}
