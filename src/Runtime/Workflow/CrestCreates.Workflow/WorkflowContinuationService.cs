using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowContinuationService : IWorkflowContinuationService
{
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly WorkflowLifecycleEventFactory _events;
    private readonly IRuntimeStateContractRegistry? _stateRegistry;

    public WorkflowContinuationService(
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowRegistry registry,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowLifecycleEventFactory events,
        IRuntimeStateContractRegistry? stateRegistry = null)
    {
        _store = store;
        _stateMachine = stateMachine;
        _registry = registry;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _events = events;
        _stateRegistry = stateRegistry;
    }

    public async Task ContinueAsync(
        WorkflowContinuationRequest request, CancellationToken ct = default)
    {
        var instance = await _store.GetByWaitingHumanTaskAsync(request.HumanTaskKey, ct)
            .ConfigureAwait(false);
        if (instance == null)
            return;

        if (instance.Status != WorkflowInstanceStatus.Suspended)
            throw new InvalidOperationException(
                $"Instance '{instance.InstanceId}' is not Suspended (status: {instance.Status}).");

        _stateMachine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running);

        var descriptor = _registry.GetByVersion(instance.Workflow.Id, instance.Workflow.Version);
        if (descriptor == null)
            throw new InvalidOperationException(
                $"Workflow '{instance.Workflow.Id}' version {instance.Workflow.Version} not found.");

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

        var currentStep = descriptor.Steps[instance.StepIndex];
        var resumedFromStatus = instance.Status;
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = currentStep.Id,
            StepName = currentStep.Name,
            Status = StepExecutionStatus.Completed,
            Output = request.Result,
            ExecutedAt = DateTimeOffset.UtcNow
        });

        instance.Variables["lastStepOutcome"] = _stateRegistry?.Capture(request.Outcome)
            ?? throw new RuntimeStateContractException("Runtime state registry is required for continuation state capture.");
        if (request.Result is not null)
            instance.Variables["lastStepResult"] = request.Result;
        instance.StepIndex++;
        instance.WaitingHumanTaskKey = null;
        instance.Status = WorkflowInstanceStatus.Running;
        var resumedPreviousId = instance.LastLifecycleAuditId;
        var resumedIdentity = _events.AllocateLifecycleIdentity();
        instance.LastLifecycleAuditId = resumedIdentity.AuditId;

        try
        {
            var expectedRevision = instance.Revision;
            await _store.UpdateAsync(instance, expectedRevision, ct).ConfigureAwait(false);
            instance.Revision = expectedRevision + 1;
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
            instance,
            descriptor,
            resumedIdentity,
            runOperationId,
            resumedFromStatus,
            request.TriggerOperationId ?? request.CompletionEventId,
            request.TriggerAuditId,
            resumedPreviousId,
            humanTaskInstanceId: request.HumanTaskKey.InstanceId,
            humanTaskCompletionEventId: request.CompletionEventId), CancellationToken.None).ConfigureAwait(false);

        await _executionRunner.RunAsync(instance, runOperationId, parent?.EnclosingAuditId, ct).ConfigureAwait(false);
    }
}
