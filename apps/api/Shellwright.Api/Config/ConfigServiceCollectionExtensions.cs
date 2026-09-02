using Microsoft.Extensions.Options;
using Shellwright.Api.Assets;
using Shellwright.Api.Endpoints;

namespace Shellwright.Api.Config;

/// <summary>Registers configuration handling and asset storage.</summary>
public static class ConfigServiceCollectionExtensions
{
    /// <summary>Adds validation, hashing, storage, and the guards around them.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightConfig(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BuildContextOptions>()
            .Bind(configuration.GetSection(BuildContextOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AssetStorageOptions>()
            .Bind(configuration.GetSection(AssetStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<HashContextProvider>();
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton<UrlSafety>();
        services.AddSingleton<IAssetBlobStore, FileSystemAssetBlobStore>();

        services.AddScoped<IAssetResolverFactory, DatabaseAssetResolverFactory>();
        services.AddScoped<ConfigService>();
        services.AddScoped<Idempotency>();

        return services;
    }
}
