using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Event;

public static class EventServiceCollectionExtensions
{
    public static IServiceCollection AddEventKernel(this IServiceCollection services)
    {
        // Same-instance bridging: concrete first, then interface resolves to same instance.
        // EventRegistryBootstrapper constructor takes EventRegistry (concrete).
        services.TryAddSingleton<EventRegistry>();
        services.TryAddSingleton<IEventRegistry>(sp => sp.GetRequiredService<EventRegistry>());
        services.TryAddSingleton<IEventMetadataProvider>(sp => sp.GetRequiredService<EventRegistry>());

        // Validation engine
        services.TryAddSingleton<IRegistryValidationEngine<GeneratedEventDescriptor>,
            RegistryValidationEngine<GeneratedEventDescriptor>>();

        // Registry build-time validators (duplicate name+version, active version chains, payload uniqueness).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRegistryValidator<GeneratedEventDescriptor>,
            DuplicateNameVersionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRegistryValidator<GeneratedEventDescriptor>,
            EventVersionChainValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRegistryValidator<GeneratedEventDescriptor>,
            UniquePayloadTypeValidator>());

        // Resolver (generated + dynamic). The shared IEventValidator slot stays untouched: transports
        // own their canonical validation boundary (RabbitMQ constructs its own RegistryEventValidator),
        // and plain LocalEvent hosts keep their existing no-governance default.
        services.TryAddSingleton<IDynamicEventRegistry, DynamicEventRegistry>();
        services.TryAddSingleton<IEventResolver, EventResolver>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, EventBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();

        return services;
    }
}
