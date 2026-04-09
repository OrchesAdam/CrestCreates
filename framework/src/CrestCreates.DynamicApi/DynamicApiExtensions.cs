using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        services.AddSingleton<IDynamicApiScanner, DynamicApiScanner>();
        services.AddSingleton(sp => sp.GetRequiredService<IDynamicApiScanner>().Scan(sp.GetRequiredService<DynamicApiOptions>()));
        services.AddScoped<DynamicApiEndpointExecutor>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<SwaggerGenOptions>, DynamicApiSwaggerGenOptionsSetup>());

        return services;
    }

    public static IEndpointRouteBuilder MapCrestDynamicApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var registry = endpoints.ServiceProvider.GetRequiredService<DynamicApiRegistry>();
        foreach (var service in registry.Services)
        {
            foreach (var action in service.Actions)
            {
                var routeBuilder = endpoints.MapMethods(
                    action.FullRoute,
                    new[] { action.HttpMethod },
                    async (HttpContext context, DynamicApiEndpointExecutor executor) =>
                        await executor.ExecuteAsync(context, service, action));

                routeBuilder.WithDisplayName($"{service.ServiceName}.{action.ActionName}");
                routeBuilder.ExcludeFromDescription();
            }
        }

        return endpoints;
    }
}
