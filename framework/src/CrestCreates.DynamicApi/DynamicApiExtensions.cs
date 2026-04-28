using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CrestCreates.DynamicApi;

public static class DynamicApiExtensions
{
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
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPostConfigureOptions<SwaggerGenOptions>, DynamicApiSwaggerGenOptionsSetup>());

        return services;
    }

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
