using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;

namespace CrestCreates.Workflow;

internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;
    private readonly WorkflowLifecycleEventFactory _events;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly IRuntimeDescriptorPinResolver<WorkflowDescriptor> _pinResolver;
    private readonly WorkflowSuspensionCommitter _suspensionCommitter;
    private readonly IDescriptorSnapshotStore? _snapshots;
    private readonly WorkflowAccountabilityOutboxAppender? _accountabilityOutbox;
    private readonly IRuntimeTransactionCoordinator? _transactions;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowLifecycleEventPublisher eventPublisher,
        WorkflowLifecycleEventFactory events,
        IRuntimeStateContractRegistry stateRegistry,
        IRuntimeDescriptorPinResolver<WorkflowDescriptor> pinResolver,
        WorkflowSuspensionCommitter suspensionCommitter,
        IDescriptorSnapshotStore? snapshots,
        WorkflowAccountabilityOutboxAppender? accountabilityOutbox = null,
        IRuntimeTransactionCoordinator? transactions = null)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
        _eventPublisher = eventPublisher;
        _events = events;
        _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));
        _pinResolver = pinResolver ?? throw new ArgumentNullException(nameof(pinResolver));
        _suspensionCommitter = suspensionCommitter ?? throw new ArgumentNullException(nameof(suspensionCommitter));
        _snapshots = snapshots;
        _accountabilityOutbox = accountabilityOutbox;
        _transactions = transactions;
    }

    public async Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        string workflowRunOperationId,
        string? enclosingAuditId,
        CancellationToken ct)
    {
        instance = instance.Snapshot();
        await RuntimeDescriptorPinEvidence.ValidateAsync(instance.WorkflowPin, _snapshots, ct).ConfigureAwait(false);
        var descriptor = _pinResolver.Resolve(instance.WorkflowPin).Descriptor;
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
            var context = new WorkflowExecutionContext(descriptor, instance, step, runOperationId);

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
                var failedExceptionEvent = _events.Create("workflow.failed", instance, descriptor, failedExceptionIdentity, runOperationId, failedFromStatus, runOperationId, parentAuditId, failedExceptionPreviousId, "WORKFLOW_STEP_EXECUTION_FAILED", step.Id, instance.WaitingHumanTaskId);
                await PersistWithAccountabilityAsync(instance, failedExceptionEvent, ct).ConfigureAwait(false);
                await _eventPublisher.PublishAsync(failedExceptionEvent, CancellationToken.None).ConfigureAwait(false);
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
                    if (string.IsNullOrWhiteSpace(stepResult.WaitingHumanTaskId)
                        || stepResult.PreparedHumanTask is null
                        || !string.Equals(stepResult.WaitingHumanTaskId, stepResult.PreparedHumanTask.Id, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "Suspended HumanTask step must provide one matching detached HumanTask candidate.");
                    var suspensionBefore = instance.Snapshot();
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
                    var suspendedEvent = _events.Create("workflow.suspended", instance, descriptor, suspendedIdentity, runOperationId, suspendedFromStatus, runOperationId, parentAuditId, suspendedPreviousId, null, step.Id, instance.WaitingHumanTaskId);
                    var suspendedMessage = _accountabilityOutbox is null ? null : await _accountabilityOutbox.PrepareAsync(suspendedEvent, ct).ConfigureAwait(false);
                    await _suspensionCommitter.CommitAsync(
                        suspensionBefore,
                        instance,
                        stepResult.PreparedHumanTask,
                        $"{runOperationId}:suspend:{step.Id}",
                        suspendedMessage,
                        ct).ConfigureAwait(false);
                    instance.Revision = suspensionBefore.Revision + 1;
                    await _eventPublisher.PublishAsync(suspendedEvent, CancellationToken.None).ConfigureAwait(false);
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
                    var failedEvent = _events.Create("workflow.failed", instance, descriptor, failedIdentity, runOperationId, failedFromStatusForStep, runOperationId, parentAuditId, failedPreviousId, "WORKFLOW_STEP_FAILED", step.Id, instance.WaitingHumanTaskId);
                    await PersistWithAccountabilityAsync(instance, failedEvent, ct).ConfigureAwait(false);
                    await _eventPublisher.PublishAsync(failedEvent, CancellationToken.None).ConfigureAwait(false);
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
        var completedEvent = _events.Create("workflow.completed", instance, descriptor, completedIdentity, runOperationId, completedFromStatus, runOperationId, parentAuditId, completedPreviousAuditId, null, null, instance.WaitingHumanTaskId);
        await PersistWithAccountabilityAsync(instance, completedEvent, ct).ConfigureAwait(false);
        await _eventPublisher.PublishAsync(completedEvent, CancellationToken.None).ConfigureAwait(false);
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

    private async Task PersistWithAccountabilityAsync(WorkflowInstance instance, WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        var message = _accountabilityOutbox is null
            ? null
            : await _accountabilityOutbox.PrepareAsync(lifecycleEvent, ct).ConfigureAwait(false);
        if (message is not null && _transactions is null)
            throw new InvalidOperationException("Reliable Workflow Accountability requires the Runtime transaction coordinator.");
        if (message is null)
        {
            await PersistUpdateAsync(instance, ct).ConfigureAwait(false);
            return;
        }
        var transactions = _transactions!;
        var appender = _accountabilityOutbox!;
        await transactions.ExecuteAsync(async transactionCt =>
        {
            await PersistUpdateAsync(instance, transactionCt).ConfigureAwait(false);
            await appender.AppendAsync(message, transactionCt).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private RuntimeStateValue? CaptureState(object? value)
    {
        if (value is null)
            return null;
        if (value is RuntimeStateValue envelope)
        {
            _stateRegistry.Validate(envelope);
            return envelope;
        }
        if (value is IReadOnlyDictionary<string, object?> dictionary)
        {
            var bag = new RuntimeStateBag(dictionary.Select(pair =>
            {
                var captured = CaptureState(pair.Value)
                    ?? throw new RuntimeStateContractException(
                        $"Workflow state dictionary entry '{pair.Key}' cannot be an untyped null.");
                return new KeyValuePair<string, RuntimeStateValue>(pair.Key, captured);
            }));
            return _stateRegistry.Capture(bag);
        }
        return _stateRegistry.Capture(value);
    }
}
