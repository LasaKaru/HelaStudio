namespace Shellwright.Api.Authorization;

/// <summary>Registers resource-based authorisation.</summary>
public static class AuthorizationServiceCollectionExtensions
{
    /// <summary>Adds the access guard and what it depends on.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<AccessGuard>();

        return services;
    }
}
