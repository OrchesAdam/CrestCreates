using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
        services.TryAddSingleton<CapabilityStepExecutor>();
        services.TryAddSingleton<HumanTaskStepExecutor>();
        services.TryAddSingleton<IWorkflowStepExecutorRegistry, DefaultStepExecutorRegistry>();
        services.TryAddSingleton<WorkflowCompatibilityValidator>();

        services.TryAddSingleton<IWorkflowStateMachine, DefaultWorkflowStateMachine>();
        services.TryAddSingleton<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
        services.TryAddSingleton<IWorkflowExecutionRunner, WorkflowExecutionRunner>();
        services.TryAddSingleton<IWorkflowContinuationService, WorkflowContinuationService>();

        services.TryAddSingleton<IWorkflowEngine>(sp =>
            new WorkflowEngine(
                sp.GetRequiredService<IWorkflowRegistry>(),
                sp.GetRequiredService<IWorkflowInstanceStore>(),
                sp.GetRequiredService<IWorkflowExecutionRunner>(),
                sp.GetRequiredService<IWorkflowLifecycleEventPublisher>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            ILocalEventHandler<HumanTaskCompletedEvent>,
            HumanTaskCompletedWorkflowSubscriber>());

        return services;
    }
}
