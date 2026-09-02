using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Shellwright.Api.Data;

/// <summary>Registers the data layer.</summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>Adds the database context, tenant scope, and connection interceptor.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightData(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<TenantContext>();
        services.AddScoped<TenantConnectionInterceptor>();

        services.AddDbContext<ShellwrightDbContext>((provider, options) =>
        {
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options.UseNpgsql(database.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__migrations"));

            // Scoped, because the identity it stamps is per request. Resolving
            // it here rather than registering it globally is what keeps that
            // true — a singleton interceptor would capture the first request's
            // tenant and serve it to everyone afterwards.
            options.AddInterceptors(provider.GetRequiredService<TenantConnectionInterceptor>());

            // ⚠️ Reads never track. The control plane's write paths are small
            // and explicit; its read paths are the hot ones, and change
            // tracking on a config body is a deep clone of the whole document.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        return services;
    }
}
