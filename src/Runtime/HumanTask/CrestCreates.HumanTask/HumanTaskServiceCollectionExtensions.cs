using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Metadata.Runtime;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;

namespace CrestCreates.HumanTask;

public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton(new HumanTaskDeliveryOptions());
        services.TryAddSingleton<OptionalCompatibilityExecutionTracker>();
        services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        services.TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>();
        services.AddOutboxDeliveryHandler<HumanTaskCompletedOutboxHandler>(HumanTaskDeliveryConstants.CompletedContractId);
        services.TryAddSingleton<IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent>, EmptyOutboxRequiredConsumerResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxDurableCompositionCheck, HumanTaskCompletionObligationCompositionCheck>());

        // HumanTask Registry (for binding status contributors)
        services.TryAddSingleton<IHumanTaskRegistry, HumanTaskRegistry>();
        services.TryAddSingleton<IRuntimeDescriptorPinResolver<HumanTaskDescriptor>>(sp =>
            new RuntimeDescriptorPinResolver<HumanTaskDescriptor>(
                sp.GetRequiredService<IHumanTaskRegistry>(),
                sp.GetRequiredService<IDescriptorStableHashBuilder>(),
                "humantask",
                DescriptorKind.HumanTask));
        services.TryAddSingleton<IRegistryValidationEngine<HumanTaskDescriptor>,
            RegistryValidationEngine<HumanTaskDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, HumanTaskBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, HumanTaskRelationshipExtractor>();

        return services;
    }

    public static IServiceCollection AddHumanTaskCompletionObligation(
        this IServiceCollection services,
        string descriptorId,
        int descriptorVersion,
        string requiredConsumerId)
    {
        if (string.IsNullOrWhiteSpace(descriptorId) || descriptorVersion <= 0 || string.IsNullOrWhiteSpace(requiredConsumerId))
            throw new ArgumentException("HumanTask obligation policy fields must be non-blank and version must be positive.");
        services.AddSingleton(new HumanTaskCompletionObligationPolicyRegistration(descriptorId, descriptorVersion, requiredConsumerId));
        return services;
    }
}
