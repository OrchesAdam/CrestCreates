using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Host-side composition of the authoritative <see cref="ICapabilityRegistry"/> from the host's static
/// catalog plus exactly the ExplicitHost contributions this host activated. The rule lives here so a
/// host cannot build the registry from only one of the two sources.
/// </summary>
public static class CapabilityRegistryComposition
{
    /// <summary>
    /// Builds the registered capability registry from the process provider bag, the host catalog and
    /// the activated contributions. Must run after <c>builder.Build()</c> and before endpoint mapping.
    /// A registry that another bootstrap already built is verified against the host catalog instead of
    /// being trusted; a missing host capability fails closed.
    /// </summary>
    public static void BuildFromHostCatalog(
        this IServiceProvider services,
        IReadOnlyList<CapabilityDescriptor> hostCatalog)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hostCatalog);

        var registry = services.GetRequiredService<ICapabilityRegistry>();
        if (registry is not CapabilityRegistry concrete)
        {
            throw new InvalidOperationException(
                "Host capability aggregation requires the concrete CapabilityRegistry; the registered " +
                "ICapabilityRegistry implementation is '" + registry.GetType().FullName + "'.");
        }

        var contributed = services.GetServices<CapabilityEndpointContribution>()
            .SelectMany(contribution => contribution.CapabilityDescriptors)
            .ToList();

        concrete.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>()
            .Concat(new IDescriptorProvider<CapabilityDescriptor>[]
            {
                new FixedDescriptorProvider(hostCatalog),
                new FixedDescriptorProvider(contributed),
            }));

        foreach (var descriptor in hostCatalog)
        {
            var registered = concrete.GetByNameAndVersion(descriptor.Name, descriptor.Version);
            if (registered is null || !string.Equals(registered.Id, descriptor.Id, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The capability registry was built before the host catalog was composed; capability '" +
                    descriptor.Name + "' v" + descriptor.Version + " is missing.");
            }
        }
    }

    private sealed class FixedDescriptorProvider(IReadOnlyList<CapabilityDescriptor> descriptors)
        : IDescriptorProvider<CapabilityDescriptor>
    {
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => descriptors;
    }
}
