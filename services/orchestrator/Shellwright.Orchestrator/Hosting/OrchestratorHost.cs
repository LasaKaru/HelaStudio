using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shellwright.Orchestrator.Activities;
using Shellwright.Orchestrator.Artifacts;
using Shellwright.Orchestrator.Logs;
using Shellwright.Orchestrator.Patching;
using Shellwright.Orchestrator.Persistence;
using Shellwright.Orchestrator.Runner;
using Shellwright.Orchestrator.Verification;
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
        services.AddShellwrightRunner(configuration);

        return services;
    }

    /// <summary>Adds the runner pool, artifact storage, patching, and verification.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// ⚠️ <c>IBuildSandbox</c> is deliberately not registered here. There
    /// are two implementations with very different security properties, and
    /// choosing between them by configuration is how a deployment ends up
    /// running customer configurations on the host without anybody deciding to.
    /// The composition root picks one, in the open.
    /// </remarks>
    public static IServiceCollection AddShellwrightRunner(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RunnerPoolOptions>()
            .Bind(configuration.GetSection(RunnerPoolOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ArtifactStorageOptions>()
            .Bind(configuration.GetSection(ArtifactStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<VerificationOptions>()
            .Bind(configuration.GetSection(VerificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<BuildStoreOptions>()
            .Bind(configuration.GetSection(BuildStoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IBuildStore, PostgresBuildStore>();
        services.AddSingleton<IArtifactCache, PostgresArtifactCache>();

        services.AddSingleton<IRunnerPool, LocalRunnerPool>();
        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.SectionName));

        // ⚠️ Chosen by whether object storage is configured, and the fallback is
        // the local one. A deployment that forgot to configure R2 gets a
        // filesystem store and a disk that fills — which is visible — rather
        // than a startup failure that takes the build fleet down, or worse, an
        // object store pointed at a bucket that does not exist.
        //
        // The two are interchangeable because an artifact reference is a
        // content address rather than a URL, so moving between them rewrites
        // nothing.
        var objectStorage = configuration.GetSection(ObjectStorageOptions.SectionName)
            .Get<ObjectStorageOptions>();

        if (!string.IsNullOrWhiteSpace(objectStorage?.ServiceUrl))
        {
            services.AddSingleton(_ => ObjectStoreClientFactory.Create(objectStorage));
            services.AddSingleton<IArtifactStore, ObjectStoreArtifactStore>();
        }
        else
        {
            services.AddSingleton<IArtifactStore, FileSystemArtifactStore>();
        }
        services.AddSingleton<IArtifactVerifier, AndroidArtifactVerifier>();
        // ⚠️ From configuration, so a runner can pin a build-tools version
        // rather than inheriting whatever provisioning left on PATH.
        services.AddSingleton(provider =>
            new AndroidToolchain(configuration["Sandbox:AndroidBuildToolsPath"]));

        services.AddSingleton<IArtifactPatcher, AndroidContentPatcher>();

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
