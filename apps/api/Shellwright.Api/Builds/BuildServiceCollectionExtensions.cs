using Microsoft.Extensions.Options;
using Temporalio.Client;

namespace Shellwright.Api.Builds;

/// <summary>Registers the build API's services.</summary>
public static class BuildServiceCollectionExtensions
{
    /// <summary>Adds build launching, workflow access, and artifact links.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightBuilds(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BuildOptions>()
            .Bind(configuration.GetSection(BuildOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WorkflowOptions>()
            .Bind(configuration.GetSection(WorkflowOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ⚠️ Connected lazily, and a failure to reach Temporal must not stop
        // the API from starting. Reading an app, saving a configuration and
        // signing in have nothing to do with builds; taking the whole control
        // plane down because the build fleet is unreachable turns one outage
        // into a total one.
        services.AddSingleton<ITemporalClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<WorkflowOptions>>().Value;

            return TemporalClient.ConnectAsync(new TemporalClientConnectOptions(settings.Address)
            {
                Namespace = settings.Namespace,
            }).GetAwaiter().GetResult();
        });

        services.AddOptions<ArtifactDownloadOptions>()
            .Bind(configuration.GetSection(ArtifactDownloadOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IBuildWorkflowClient, TemporalBuildWorkflowClient>();
        services.AddScoped<IArtifactBytes, FileSystemArtifactBytes>();
        services.AddSingleton<ArtifactLinks>();
        services.AddScoped<BuildLauncher>();

        return services;
    }
}
