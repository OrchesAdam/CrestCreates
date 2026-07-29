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
    private readonly WorkflowLifecycleEventFactory _events;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowLifecycleEventPublisher eventPublisher,
        WorkflowLifecycleEventFactory events)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
        _eventPublisher = eventPublisher;
        _events = events;
    }

    public async Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        string workflowRunOperationId,
        string? enclosingAuditId,
        CancellationToken ct)
    {
        var descriptor = _registry.GetByVersion(instance.Workflow.Id, instance.Workflow.Version);
        if (descriptor == null)
            throw new InvalidOperationException(
                $"Workflow '{instance.Workflow.Id}' version {instance.Workflow.Version} not found.");

        return await ExecuteStepsAsync(instance, descriptor, workflowRunOperationId, enclosingAuditId, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        string runOperationId,
        string? parentAuditId,
        CancellationToken ct)
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
                var failedFromStatus = instance.Status;
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
                var failedExceptionPreviousId = instance.LastLifecycleAuditId;
                var failedExceptionIdentity = _events.AllocateLifecycleIdentity();
                instance.LastLifecycleAuditId = failedExceptionIdentity.AuditId;
                await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                await PublishEvent("workflow.failed", instance, descriptor, runOperationId, parentAuditId, failedFromStatus, failedExceptionPreviousId, failedExceptionIdentity, "WORKFLOW_STEP_EXECUTION_FAILED", step.Id, CancellationToken.None).ConfigureAwait(false);
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
                    var suspendedFromStatus = instance.Status;
                    instance.WaitingHumanTaskId = stepResult.WaitingHumanTaskId;
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Suspended);
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    var suspendedPreviousId = instance.LastLifecycleAuditId;
                    var suspendedIdentity = _events.AllocateLifecycleIdentity();
                    instance.LastLifecycleAuditId = suspendedIdentity.AuditId;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    await PublishEvent("workflow.suspended", instance, descriptor, runOperationId, parentAuditId, suspendedFromStatus, suspendedPreviousId, suspendedIdentity, null, step.Id, CancellationToken.None).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    if (step.OnError == StepErrorBehavior.Skip)
                    { instance.StepIndex++; continue; }
                    var failedFromStatusForStep = instance.Status;
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = $"Step '{step.Id}' failed.";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    var failedPreviousId = instance.LastLifecycleAuditId;
                    var failedIdentity = _events.AllocateLifecycleIdentity();
                    instance.LastLifecycleAuditId = failedIdentity.AuditId;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    await PublishEvent("workflow.failed", instance, descriptor, runOperationId, parentAuditId, failedFromStatusForStep, failedPreviousId, failedIdentity, "WORKFLOW_STEP_FAILED", step.Id, CancellationToken.None).ConfigureAwait(false);
                    return instance;
            }
        }

        var completedFromStatus = instance.Status;
        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        var completedPreviousAuditId = instance.LastLifecycleAuditId;
        var completedIdentity = _events.AllocateLifecycleIdentity();
        instance.LastLifecycleAuditId = completedIdentity.AuditId;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        await PublishEvent("workflow.completed", instance, descriptor, runOperationId, parentAuditId, completedFromStatus, completedPreviousAuditId, completedIdentity, null, null, CancellationToken.None).ConfigureAwait(false);
        return instance;
    }

    private Task PublishEvent(
        string eventType,
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        string causationId,
        string? parentAuditId,
        WorkflowInstanceStatus fromStatus,
        string? previousAuditId,
        WorkflowLifecycleIdentity identity,
        string? reasonCode,
        string? stepId,
        CancellationToken ct)
    {
        return _eventPublisher.PublishAsync(_events.Create(
            eventType,
            instance,
            descriptor,
            identity,
            causationId,
            fromStatus,
            causationId,
            parentAuditId,
            previousAuditId,
            reasonCode,
            stepId,
            instance.WaitingHumanTaskId), ct);
    }
}
