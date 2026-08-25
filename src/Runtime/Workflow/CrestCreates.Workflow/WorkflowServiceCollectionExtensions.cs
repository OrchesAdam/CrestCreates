using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Abstractions.Bootstrap;
using CrestCreates.Metadata.Registry;
using CrestCreates.Metadata.Runtime;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using CrestCreates.Runtime.Delivery;

namespace CrestCreates.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.TryAddScoped<CapabilityStepExecutor>(sp =>
            new CapabilityStepExecutor(
                sp.GetRequiredService<CrestCreates.Capability.Abstractions.ICapabilityPipeline>(),
                sp.GetService<IRuntimeStateContractRegistry>()));
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
        services.TryAddScoped<WorkflowSuspensionCommitter>();
        services.TryAddSingleton<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>(sp =>
            new RuntimeDescriptorPinResolver<WorkflowDescriptor>(
                sp.GetRequiredService<IWorkflowRegistry>(),
                sp.GetRequiredService<IDescriptorStableHashBuilder>(),
                "workflow",
                DescriptorKind.Workflow));
        services.TryAddSingleton(new WorkflowPostCommitNotificationOptions());
        services.TryAddSingleton<IWorkflowPostCommitNotificationBudget, DefaultWorkflowPostCommitNotificationBudget>();
        services.TryAddScoped<WorkflowLifecycleEventFactory>();
        services.TryAddScoped<WorkflowAccountabilityOutboxAppender>(sp => new WorkflowAccountabilityOutboxAppender(
            sp.GetService<CrestCreates.Accountability.Abstractions.Preparation.IAuditEnvelopePreparer>(),
            sp.GetService<CrestCreates.Runtime.Delivery.Abstractions.Stores.ITransactionalOutboxWriter>(),
            sp.GetService<CrestCreates.Runtime.Delivery.Abstractions.Messages.IOutboxMessageFactory>()));
        services.TryAddScoped<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
        services.TryAddScoped<IWorkflowLifecycleObserver, WorkflowAccountabilityObserver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBootstrapValidator, WorkflowAccountabilityCompositionValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WorkflowAccountabilityCompositionValidator>());
        services.TryAddScoped<IWorkflowExecutionRunner>(sp => new WorkflowExecutionRunner(
            sp.GetRequiredService<IWorkflowRegistry>(),
            sp.GetRequiredService<IWorkflowStepExecutorRegistry>(),
            sp.GetRequiredService<IWorkflowInstanceStore>(),
            sp.GetRequiredService<IWorkflowStateMachine>(),
            sp.GetRequiredService<IWorkflowLifecycleEventPublisher>(),
            sp.GetRequiredService<WorkflowLifecycleEventFactory>(),
            sp.GetRequiredService<IRuntimeStateContractRegistry>(),
            sp.GetRequiredService<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>(),
            sp.GetRequiredService<WorkflowSuspensionCommitter>(),
            sp.GetService<IDescriptorSnapshotStore>(),
            sp.GetRequiredService<WorkflowAccountabilityOutboxAppender>(),
            sp.GetService<IRuntimeTransactionCoordinator>()));
        services.TryAddScoped<IWorkflowContinuationService>(sp => new WorkflowContinuationService(
            sp.GetRequiredService<IWorkflowInstanceStore>(),
            sp.GetRequiredService<IWorkflowStateMachine>(),
            sp.GetRequiredService<IWorkflowExecutionRunner>(),
            sp.GetRequiredService<IWorkflowLifecycleEventPublisher>(),
            sp.GetRequiredService<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(),
            sp.GetRequiredService<WorkflowLifecycleEventFactory>(),
            sp.GetRequiredService<IRuntimeStateContractRegistry>(),
            sp.GetRequiredService<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>(),
            sp.GetService<IDescriptorSnapshotStore>(),
            sp.GetService<IWorkflowContinuationAcceptanceStore>(),
            sp.GetService<IRuntimeTransactionCoordinator>(),
            sp.GetService<CrestCreates.Accountability.Abstractions.Preparation.IAuditEnvelopePreparer>(),
            sp.GetService<CrestCreates.Runtime.Delivery.Abstractions.Stores.ITransactionalOutboxWriter>(),
            sp.GetService<CrestCreates.Runtime.Delivery.Abstractions.Messages.IOutboxMessageFactory>(),
            sp.GetService<Microsoft.Extensions.Logging.ILogger<WorkflowContinuationService>>()));

        services.TryAddScoped<IWorkflowEngine>(sp =>
            new WorkflowEngine(
                sp.GetRequiredService<IWorkflowRegistry>(),
                sp.GetRequiredService<IWorkflowInstanceStore>(),
                sp.GetRequiredService<IWorkflowExecutionRunner>(),
                sp.GetRequiredService<IWorkflowLifecycleEventPublisher>(),
                sp.GetRequiredService<CrestCreates.Accountability.Abstractions.Context.IAuditOperationContextAccessor>(),
                sp.GetRequiredService<WorkflowLifecycleEventFactory>(),
                sp.GetRequiredService<IRuntimeDescriptorPinResolver<WorkflowDescriptor>>(),
                sp.GetRequiredService<IRuntimeStateContractRegistry>(),
                sp.GetService<IRuntimeTransactionCoordinator>(),
                sp.GetRequiredService<WorkflowAccountabilityOutboxAppender>()));

        services.AddOutboxRequiredConsumer<HumanTaskCompletedEvent, WorkflowContinuationOutboxConsumer>(HumanTaskDeliveryConstants.WorkflowContinuationConsumerId);

        return services;
    }
}
