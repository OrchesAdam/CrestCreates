using CrestCreates.Capability.Abstractions;
using CrestCreates.Generated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.Procurement.Application;

public static class ProcurementCapabilityServiceCollectionExtensions
{
    /// <summary>
    /// Activates the compile-time generated Procurement capability handlers: DI registration of every
    /// handler (constructor injection supplies the application service and orchestrator) plus the
    /// generated provider definition applied to the Host-owned resolver.
    /// </summary>
    public static IServiceCollection AddProcurementCapabilityHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ICapabilityHandlerModule>(GeneratedCapabilityHandlerModule.Instance));
        GeneratedHandlerRegistry.RegisterServices(services);

        return services;
    }
}
