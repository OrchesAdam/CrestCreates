using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

internal sealed class CapabilityEndpointRegistryBootstrapper
{
    private readonly ICapabilityEndpointRegistry _registry;
    private int _built;

    public CapabilityEndpointRegistryBootstrapper(ICapabilityEndpointRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void EnsureBuilt()
    {
        if (Interlocked.Exchange(ref _built, 1) != 0)
            return;

        var providers = DescriptorProviderRegistry.GetProviders<CapabilityEndpointDescriptor>();
        _registry.Build(providers);
    }
}
