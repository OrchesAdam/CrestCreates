using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;

namespace CrestCreates.Workflow;

/// <summary>
/// Owns the cross-store compensation boundary for a suspended Workflow wait.
/// It is intentionally separate from the continuation runner: abort is an
/// administrative terminal transition, not a resumed Workflow execution.
/// </summary>
public sealed class WorkflowAbortService : IWorkflowAbortService
{
    private readonly IWorkflowInstanceStore _workflows;
    private readonly IHumanTaskInstanceStore _humanTasks;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IRuntimeDescriptorPinResolver<WorkflowDescriptor> _pinResolver;
    private readonly IRuntimeTransactionCoordinator _transactions;
    private readonly WorkflowLifecycleEventFactory _events;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly WorkflowAccountabilityOutboxAppender _accountabilityOutbox;

    internal WorkflowAbortService(
        IWorkflowInstanceStore workflows,
        IHumanTaskInstanceStore humanTasks,
        IWorkflowStateMachine stateMachine,
        IRuntimeDescriptorPinResolver<WorkflowDescriptor> pinResolver,
        IRuntimeTransactionCoordinator transactions,
        WorkflowLifecycleEventFactory events,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowAccountabilityOutboxAppender accountabilityOutbox)
    {
        _workflows = workflows;
        _humanTasks = humanTasks;
        _stateMachine = stateMachine;
        _pinResolver = pinResolver;
        _transactions = transactions;
        _events = events;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _accountabilityOutbox = accountabilityOutbox;
    }

    public async Task AbortAsync(
        RuntimeInstanceKey workflowKey,
        RuntimeInstanceKey humanTaskKey,
        string reason,
        CancellationToken cancellationToken = default)
    {
        workflowKey.EnsureValid();
        humanTaskKey.EnsureValid();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Workflow abort reason is required.", nameof(reason));
        if (workflowKey.TenantId != humanTaskKey.TenantId)
            throw new InvalidOperationException("Workflow and HumanTask abort keys must share the same tenant scope.");

        WorkflowLifecycleEvent? failedEvent = null;
        var operationId = _events.CreateRunOperationId();
        using var scope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = operationId,
            OperationId = operationId,
            Actor = new AuditActor { Kind = "workflow", Id = workflowKey.InstanceId },
            TenantId = workflowKey.TenantId,
            InvocationSource = "workflow"
        });

        await _transactions.ExecuteAsync(async transactionCt =>
        {
            var workflow = await _workflows.GetAsync(workflowKey, transactionCt).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Workflow instance '{workflowKey.InstanceId}' was not found.");
            var task = await _humanTasks.GetAsync(humanTaskKey, transactionCt).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"HumanTask instance '{humanTaskKey.InstanceId}' was not found.");

            if (workflow.Status != WorkflowInstanceStatus.Suspended
                || workflow.WaitingHumanTaskKey != humanTaskKey
                || task.WorkflowKey != workflowKey)
                throw new InvalidOperationException("Workflow abort requires a reciprocal suspended HumanTask wait.");
            if (task.Status is HumanTaskInstanceStatus.Completed or HumanTaskInstanceStatus.Cancelled)
                throw new InvalidOperationException("Workflow abort cannot consume a terminal HumanTask.");

            var descriptor = _pinResolver.Resolve(workflow.WorkflowPin).Descriptor;
            var candidate = workflow.Snapshot();
            var fromStatus = candidate.Status;
            _stateMachine.ValidateTransition(fromStatus, WorkflowInstanceStatus.Failed);
            candidate.Status = WorkflowInstanceStatus.Failed;
            candidate.ErrorMessage = reason;
            candidate.CompletedAt = DateTimeOffset.UtcNow;
            candidate.WaitingHumanTaskKey = null;
            var identity = _events.AllocateLifecycleIdentity();
            candidate.LastLifecycleAuditId = identity.AuditId;
            failedEvent = _events.Create(
                "workflow.failed",
                candidate,
                descriptor,
                identity,
                operationId,
                fromStatus,
                operationId,
                workflow.AuditOrigin?.UpstreamAuditId,
                workflow.LastLifecycleAuditId,
                "WORKFLOW_ABORTED_AFTER_BUSINESS_FAILURE",
                workflow.CurrentStepId,
                humanTaskKey.InstanceId);
            var message = await _accountabilityOutbox.PrepareAsync(failedEvent, transactionCt).ConfigureAwait(false);

            var cancelled = task.Snapshot();
            cancelled.Status = HumanTaskInstanceStatus.Cancelled;
            cancelled.CancellationReason = reason;
            cancelled.CancelledAt = DateTimeOffset.UtcNow;
            await _humanTasks.UpdateAsync(cancelled, task.Revision, transactionCt).ConfigureAwait(false);
            await _workflows.UpdateAsync(candidate, workflow.Revision, transactionCt).ConfigureAwait(false);
            if (message is not null)
                await _accountabilityOutbox.AppendAsync(message, transactionCt).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(failedEvent!, CancellationToken.None).ConfigureAwait(false);
    }
}
