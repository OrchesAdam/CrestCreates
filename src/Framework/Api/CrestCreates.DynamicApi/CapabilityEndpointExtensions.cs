using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Registry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DynamicApi;

public static class CapabilityEndpointExtensions
{
    public static IServiceCollection AddCrestCapabilityEndpoints(
        this IServiceCollection services,
        Action<CapabilityEndpointOptions>? configure = null)
    {
        services.TryAddSingleton<ICapabilityEndpointRegistry, CapabilityEndpointRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityEndpointDescriptor>,
            RegistryValidationEngine<CapabilityEndpointDescriptor>>();
        services.TryAddSingleton<CapabilityEndpointRegistryBootstrapper>();

        // Multi-registration interfaces — validators and extractors accumulate across modules
        services.AddSingleton<IRegistryValidator<CapabilityEndpointDescriptor>,
            CapabilityEndpointDescriptorValidator>();
        services.AddSingleton<IDescriptorRelationshipExtractor,
            CapabilityEndpointRelationshipExtractor>();

        var options = new CapabilityEndpointOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        return services;
    }

    public static IEndpointRouteBuilder MapCrestCapabilityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var bootstrapper = endpoints.ServiceProvider
            .GetRequiredService<CapabilityEndpointRegistryBootstrapper>();
        bootstrapper.EnsureBuilt();

        var registry = endpoints.ServiceProvider
            .GetRequiredService<ICapabilityEndpointRegistry>();
        var capabilityRegistry = endpoints.ServiceProvider
            .GetRequiredService<ICapabilityRegistry>();

        foreach (var descriptor in registry.GetAll()
            .Where(x => x.State == DescriptorState.Active))
        {
            var binding = CapabilityEndpointBindingRegistry
                .GetRequired(descriptor.Id, descriptor.Version);

            var capability = CapabilityEndpointCapabilityResolver
                .Resolve(capabilityRegistry, descriptor.Capability);

            CapabilityEndpointMapper.MapEndpoint(
                endpoints, descriptor, capability, binding);
        }

        return endpoints;
    }
}
