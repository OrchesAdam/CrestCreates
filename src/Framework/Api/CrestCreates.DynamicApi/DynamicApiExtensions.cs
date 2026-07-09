using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DynamicApi;

public static class DynamicApiExtensions
{
    /// <summary>
    /// Legacy AppService-oriented HTTP exposure path.
    /// This API is kept for AppService compatibility.
    /// New HTTP exposure should use the Capability-first endpoint projection path.
    /// Do not extend this path with Capability runtime, topology, activation,
    /// agent authoring, or MCP projection semantics.
    /// </summary>
    public static IServiceCollection AddCrestDynamicApi(
        this IServiceCollection services,
        Action<DynamicApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DynamicApiOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<DynamicApiRouteConvention>();

        services.AddSingleton(sp =>
        {
            var dynamicApiOptions = sp.GetRequiredService<DynamicApiOptions>();
            var generatedRegistry = DynamicApiGeneratedRegistryStore.BuildRegistry(dynamicApiOptions);
            if (generatedRegistry is not null)
            {
                return generatedRegistry;
            }

            throw DynamicApiGeneratedRegistryStore.CreateMissingGeneratedProviderException(dynamicApiOptions);
        });

        DynamicApiGeneratedRegistryStore.ApplyControllerRegistrations(services);

        return services;
    }

    /// <summary>
    /// Legacy AppService-oriented HTTP endpoint mapping.
    /// This API is kept for AppService compatibility.
    /// New HTTP endpoint mapping should use the Capability-first endpoint projection path.
    /// Do not extend this path with Capability runtime, topology, activation,
    /// agent authoring, or MCP projection semantics.
    /// </summary>
    public static IEndpointRouteBuilder MapCrestDynamicApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<DynamicApiOptions>();
        if (DynamicApiGeneratedRegistryStore.MapGeneratedEndpoints(endpoints, options))
        {
            return endpoints;
        }

        throw DynamicApiGeneratedRegistryStore.CreateMissingGeneratedProviderException(options);
    }
}
