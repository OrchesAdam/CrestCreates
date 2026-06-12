using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
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

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, EventBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();

        return services;
    }
}
