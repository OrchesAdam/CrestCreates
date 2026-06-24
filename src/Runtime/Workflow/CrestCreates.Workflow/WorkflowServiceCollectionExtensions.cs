using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
        services.TryAddScoped<CapabilityStepExecutor>();
        services.TryAddScoped<HumanTaskStepExecutor>();
        services.TryAddScoped<IWorkflowStepExecutorRegistry, DefaultStepExecutorRegistry>();
        services.TryAddScoped<WorkflowCompatibilityValidator>();

        // Workflow Registry (for binding status contributors)
        services.TryAddSingleton<IWorkflowRegistry, WorkflowRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<WorkflowDescriptor>,
            RegistryValidationEngine<WorkflowDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, WorkflowBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, WorkflowRelationshipExtractor>();

        services.TryAddSingleton<IWorkflowStateMachine, DefaultWorkflowStateMachine>();
        services.TryAddSingleton<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
        services.TryAddScoped<IWorkflowExecutionRunner, WorkflowExecutionRunner>();
        services.TryAddScoped<IWorkflowContinuationService, WorkflowContinuationService>();

        services.TryAddScoped<IWorkflowEngine>(sp =>
            new WorkflowEngine(
                sp.GetRequiredService<IWorkflowRegistry>(),
                sp.GetRequiredService<IWorkflowInstanceStore>(),
                sp.GetRequiredService<IWorkflowExecutionRunner>(),
                sp.GetRequiredService<IWorkflowLifecycleEventPublisher>()));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            ILocalEventHandler<HumanTaskCompletedEvent>,
            HumanTaskCompletedWorkflowSubscriber>());

        return services;
    }
}
