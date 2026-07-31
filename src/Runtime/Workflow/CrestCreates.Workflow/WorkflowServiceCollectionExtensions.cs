using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Registry;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        services.TryAddSingleton(new WorkflowPostCommitNotificationOptions());
        services.TryAddSingleton<IWorkflowPostCommitNotificationBudget, DefaultWorkflowPostCommitNotificationBudget>();
        services.TryAddScoped<WorkflowLifecycleEventFactory>();
        services.TryAddScoped<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
        services.TryAddScoped<IWorkflowLifecycleObserver, WorkflowAccountabilityObserver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, WorkflowAccountabilityCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkflowAccountabilityCompositionValidator>());
        services.TryAddScoped<IWorkflowExecutionRunner, WorkflowExecutionRunner>();
        services.TryAddScoped<IWorkflowContinuationService, WorkflowContinuationService>();

        services.TryAddScoped<IWorkflowEngine>(sp =>
            new WorkflowEngine(
                sp.GetRequiredService<IWorkflowRegistry>(),
                sp.GetRequiredService<IWorkflowInstanceStore>(),
                sp.GetRequiredService<IWorkflowExecutionRunner>(),
                sp.GetRequiredService<IWorkflowLifecycleEventPublisher>(),
                sp.GetRequiredService<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(),
                sp.GetRequiredService<WorkflowLifecycleEventFactory>()));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            ILocalEventHandler<HumanTaskCompletedEvent>,
            HumanTaskCompletedWorkflowSubscriber>());

        return services;
    }
}
