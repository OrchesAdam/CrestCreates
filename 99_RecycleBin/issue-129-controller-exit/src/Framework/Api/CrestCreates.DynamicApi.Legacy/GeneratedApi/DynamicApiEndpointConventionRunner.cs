using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

public static class DynamicApiEndpointConventionRunner
{
    public static void Apply(
        IServiceProvider serviceProvider,
        DynamicApiOptions options,
        DynamicApiEndpointConventionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var conventionType in options.EndpointConventionTypes)
        {
            var convention = (IDynamicApiEndpointConvention)serviceProvider.GetRequiredService(conventionType);
            convention.Apply(context);
        }
    }
}
