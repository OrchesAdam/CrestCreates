using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
        _eventPublisher = eventPublisher;
    }

    public async Task<WorkflowInstance> RunAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var descriptor = _registry.GetById(instance.Workflow.Id);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{instance.Workflow.Id}' not found.");

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance, WorkflowDescriptor descriptor, CancellationToken ct)
    {
        var steps = descriptor.Steps;

        while (instance.StepIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[instance.StepIndex];
            instance.CurrentStepId = step.Id;
            var startedAt = DateTimeOffset.UtcNow;

            var executor = _executorRegistry.Resolve(step.Target);
            var context = new WorkflowExecutionContext(descriptor, instance, step);

            StepExecutionResult stepResult;
            try
            {
                stepResult = await executor.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                instance.StepResults.Add(new WorkflowStepResult
                {
                    StepId = step.Id, StepName = step.Name,
                    Status = StepExecutionStatus.Failed, ErrorMessage = ex.Message,
                    ExecutedAt = DateTimeOffset.UtcNow,
                    Duration = DateTimeOffset.UtcNow - startedAt
                });
                _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                instance.Status = WorkflowInstanceStatus.Failed;
                instance.ErrorMessage = ex.Message;
                instance.CompletedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                await PublishEvent("workflow.failed", instance, descriptor.Id, ct).ConfigureAwait(false);
                return instance;
            }

            if (stepResult.Variables != null)
                foreach (var kv in stepResult.Variables)
                    instance.Variables[kv.Key] = kv.Value;

            instance.StepResults.Add(new WorkflowStepResult
            {
                StepId = step.Id, StepName = step.Name,
                Status = stepResult.Status, Output = stepResult.Output,
                ExecutedAt = DateTimeOffset.UtcNow,
                Duration = DateTimeOffset.UtcNow - startedAt
            });

            switch (stepResult.Status)
            {
                case StepExecutionStatus.Completed:
                    instance.StepIndex++;
                    continue;

                case StepExecutionStatus.Suspended:
                    if (string.IsNullOrWhiteSpace(stepResult.WaitingHumanTaskId))
                        throw new InvalidOperationException(
                            "Suspended HumanTask step must provide WaitingHumanTaskId.");
                    instance.WaitingHumanTaskId = stepResult.WaitingHumanTaskId;
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Suspended);
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    await PublishEvent("workflow.suspended", instance, descriptor.Id, ct).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    if (step.OnError == StepErrorBehavior.Skip)
                    { instance.StepIndex++; continue; }
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = $"Step '{step.Id}' failed.";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    await PublishEvent("workflow.failed", instance, descriptor.Id, ct).ConfigureAwait(false);
                    return instance;
            }
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        await PublishEvent("workflow.completed", instance, descriptor.Id, ct).ConfigureAwait(false);
        return instance;
    }

    private Task PublishEvent(string eventType, WorkflowInstance instance,
        string workflowId, CancellationToken ct)
    {
        return _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = eventType,
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = workflowId,
            Status = instance.Status
        }, ct);
    }
}
