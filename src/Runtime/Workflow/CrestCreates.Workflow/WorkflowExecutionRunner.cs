using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
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
    private readonly IRuntimeStateContractRegistry? _stateRegistry;
    private readonly IRuntimeTransactionCoordinator? _transactionCoordinator;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowLifecycleEventPublisher eventPublisher,
        WorkflowLifecycleEventFactory events,
        IRuntimeStateContractRegistry? stateRegistry = null,
        IRuntimeTransactionCoordinator? transactionCoordinator = null)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
        _eventPublisher = eventPublisher;
        _events = events;
        _stateRegistry = stateRegistry;
        _transactionCoordinator = transactionCoordinator;
    }

    public async Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        string workflowRunOperationId,
        string? enclosingAuditId,
        CancellationToken ct)
    {
        instance = instance.Snapshot();
        var descriptor = _registry.GetByVersion(instance.Workflow.Id, instance.Workflow.Version);
        if (descriptor == null)
            throw new InvalidOperationException(
                $"Workflow '{instance.Workflow.Id}' version {instance.Workflow.Version} not found.");

        if (_transactionCoordinator is null)
            return await ExecuteStepsAsync(instance, descriptor, workflowRunOperationId, enclosingAuditId, ct).ConfigureAwait(false);

        return await _transactionCoordinator.ExecuteAsync(
            token => new ValueTask<WorkflowInstance>(ExecuteStepsAsync(instance, descriptor, workflowRunOperationId, enclosingAuditId, token)),
            ct).ConfigureAwait(false);
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
                await PersistUpdateAsync(instance, ct).ConfigureAwait(false);
                await PublishEvent("workflow.failed", instance, descriptor, runOperationId, parentAuditId, failedFromStatus, failedExceptionPreviousId, failedExceptionIdentity, "WORKFLOW_STEP_EXECUTION_FAILED", step.Id, CancellationToken.None).ConfigureAwait(false);
                return instance;
            }

            if (stepResult.Variables != null)
                foreach (var kv in stepResult.Variables)
                    instance.Variables[kv.Key] = CaptureState(kv.Value)
                        ?? throw new RuntimeStateContractException("Workflow step variable cannot be untyped null.");

            instance.StepResults.Add(new WorkflowStepResult
            {
                StepId = step.Id, StepName = step.Name,
                Status = stepResult.Status, Output = CaptureState(stepResult.Output),
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
                    instance.WaitingHumanTaskKey = new RuntimeInstanceKey(
                        instance.TenantId,
                        stepResult.WaitingHumanTaskId);
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Suspended);
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = null;
                    var suspendedPreviousId = instance.LastLifecycleAuditId;
                    var suspendedIdentity = _events.AllocateLifecycleIdentity();
                    instance.LastLifecycleAuditId = suspendedIdentity.AuditId;
                    await PersistUpdateAsync(instance, ct).ConfigureAwait(false);
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
                    await PersistUpdateAsync(instance, ct).ConfigureAwait(false);
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
        await PersistUpdateAsync(instance, ct).ConfigureAwait(false);
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

    private async Task PersistUpdateAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var expectedRevision = instance.Revision;
        await _store.UpdateAsync(instance, expectedRevision, ct).ConfigureAwait(false);
        instance.Revision = expectedRevision + 1;
    }

    private RuntimeStateValue? CaptureState(object? value)
    {
        if (value is null)
            return null;
        if (value is RuntimeStateValue envelope)
            return envelope;
        if (value is IReadOnlyDictionary<string, object?> dictionary)
        {
            var bag = new RuntimeStateBag(dictionary.Select(pair =>
            {
                var captured = CaptureState(pair.Value)
                    ?? throw new RuntimeStateContractException(
                        $"Workflow state dictionary entry '{pair.Key}' cannot be an untyped null.");
                return new KeyValuePair<string, RuntimeStateValue>(pair.Key, captured);
            }));
            return _stateRegistry?.Capture(bag)
                ?? throw new RuntimeStateContractException("Runtime state registry is required for durable state capture.");
        }
        return _stateRegistry?.Capture(value)
            ?? throw new RuntimeStateContractException("Runtime state registry is required for durable state capture.");
    }
}
