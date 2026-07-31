using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowContinuationService : IWorkflowContinuationService
{
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly WorkflowLifecycleEventFactory _events;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly IRuntimeDescriptorPinResolver<WorkflowDescriptor> _pinResolver;
    private readonly IDescriptorSnapshotStore? _snapshots;

    public WorkflowContinuationService(
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowLifecycleEventFactory events,
        IRuntimeStateContractRegistry stateRegistry,
        IRuntimeDescriptorPinResolver<WorkflowDescriptor> pinResolver,
        IDescriptorSnapshotStore? snapshots)
    {
        _store = store;
        _stateMachine = stateMachine;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _events = events;
        _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));
        _pinResolver = pinResolver ?? throw new ArgumentNullException(nameof(pinResolver));
        _snapshots = snapshots;
    }

    public async Task ContinueAsync(
        WorkflowContinuationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instance = await _store.GetByWaitingHumanTaskAsync(request.HumanTaskKey, ct)
            .ConfigureAwait(false);
        if (instance == null)
            return;

        if (instance.Status != WorkflowInstanceStatus.Suspended)
            throw new InvalidOperationException(
                $"Instance '{instance.InstanceId}' is not Suspended (status: {instance.Status}).");

        _stateMachine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running);

        await RuntimeDescriptorPinEvidence.ValidateAsync(instance.WorkflowPin, _snapshots, ct).ConfigureAwait(false);
        var descriptor = _pinResolver.Resolve(instance.WorkflowPin).Descriptor;
        if (request.WorkflowKey != instance.Key)
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Workflow continuation key does not match the waiting Workflow instance.");
        if (request.Result is not null)
            _stateRegistry.Validate(request.Result);

        var runOperationId = _events.CreateRunOperationId();
        var parent = _contexts.Current;
        using var scope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = instance.AuditOrigin?.CorrelationId ?? parent?.CorrelationId ?? runOperationId,
            OperationId = runOperationId,
            EnclosingAuditId = parent?.EnclosingAuditId,
            Actor = new AuditActor
            {
                Kind = "workflow",
                Id = instance.InstanceId,
                InitiatedBy = instance.AuditOrigin is { InitiatingActor: var actor }
                    ? new AuditActorReference(actor.Kind, actor.Id)
                    : null
            },
            TenantId = instance.TenantId,
            InvocationSource = "workflow"
        });

        var candidate = instance.Snapshot();
        var currentStep = descriptor.Steps[candidate.StepIndex];
        var resumedFromStatus = candidate.Status;
        candidate.StepResults.Add(new WorkflowStepResult
        {
            StepId = currentStep.Id,
            StepName = currentStep.Name,
            Status = StepExecutionStatus.Completed,
            Output = request.Result,
            ExecutedAt = DateTimeOffset.UtcNow
        });

        candidate.Variables["lastStepOutcome"] = _stateRegistry.Capture(request.Outcome);
        if (request.Result is not null)
            candidate.Variables["lastStepResult"] = request.Result;
        candidate.StepIndex++;
        candidate.WaitingHumanTaskKey = null;
        candidate.Status = WorkflowInstanceStatus.Running;
        var resumedPreviousId = candidate.LastLifecycleAuditId;
        var resumedIdentity = _events.AllocateLifecycleIdentity();
        candidate.LastLifecycleAuditId = resumedIdentity.AuditId;

        try
        {
            var expectedRevision = instance.Revision;
            await _store.UpdateAsync(candidate, expectedRevision, ct).ConfigureAwait(false);
            candidate.Revision = expectedRevision + 1;
        }
        catch (RuntimeConcurrencyException)
        {
            // Another duplicate continuation already saved — re-query to check.
            // If WaitingHumanTaskId is already cleared (duplicate) → idempotent no-op.
            var recheck = await _store.GetByWaitingHumanTaskAsync(request.HumanTaskKey, ct)
                .ConfigureAwait(false);
            if (recheck == null)
                return; // Duplicate: another continuation already cleared it

            // Genuine concurrent conflict on unrelated save — rethrow
            throw;
        }

        await _eventPublisher.PublishAsync(_events.Create(
            "workflow.resumed",
            candidate,
            descriptor,
            resumedIdentity,
            runOperationId,
            resumedFromStatus,
            request.TriggerOperationId ?? request.CompletionEventId,
            request.TriggerAuditId,
            resumedPreviousId,
            humanTaskInstanceId: request.HumanTaskKey.InstanceId,
            humanTaskCompletionEventId: request.CompletionEventId), CancellationToken.None).ConfigureAwait(false);

        await _executionRunner.RunAsync(candidate, runOperationId, parent?.EnclosingAuditId, ct).ConfigureAwait(false);
    }
}
