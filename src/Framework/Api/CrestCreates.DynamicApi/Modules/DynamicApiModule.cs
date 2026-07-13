using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.DynamicApi.Modules;

[CrestModule]
// Requires: ICapabilityRegistry (registered by CrestCreates.Capability module).
// CapabilityEndpointDescriptorValidator depends on ICapabilityRegistry for authority checks.
// If ICapabilityRegistry is not registered, DI will fail at resolution time (fail-closed).
public class DynamicApiModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<ICapabilityEndpointRegistry, CapabilityEndpointRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<CapabilityEndpointDescriptor>,
            RegistryValidationEngine<CapabilityEndpointDescriptor>>();
        services.TryAddSingleton<ICapabilityEndpointResultContractRegistry, CapabilityEndpointResultContractRegistry>();

        // TryAddEnumerable: idempotent — safe regardless of call order with AddCrestCapabilityEndpoints()
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IRegistryValidator<CapabilityEndpointDescriptor>,
            CapabilityEndpointDescriptorValidator>());

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IDescriptorRelationshipExtractor,
            CapabilityEndpointRelationshipExtractor>());
    }
}