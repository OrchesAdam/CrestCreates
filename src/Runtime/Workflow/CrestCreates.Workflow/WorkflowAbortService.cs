using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Abstractions.Delivery;
using CrestCreates.Workflow.Accountability;

namespace CrestCreates.Workflow;

/// <summary>
/// Owns the cross-store compensation boundary for a suspended Workflow wait.
/// The HumanTask runtime remains the sole HumanTask lifecycle authority; the
/// durable abort receipt makes a committed-but-unacknowledged abort replayable.
/// </summary>
public sealed class WorkflowAbortService : IWorkflowAbortService
{
    private readonly IWorkflowInstanceStore _workflows;
    private readonly IHumanTaskInstanceStore _humanTaskStore;
    private readonly IHumanTaskRuntime _humanTasks;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IRuntimeDescriptorPinResolver<WorkflowDescriptor> _pinResolver;
    private readonly IDescriptorSnapshotStore? _snapshots;
    private readonly IRuntimeTransactionCoordinator _transactions;
    private readonly IWorkflowAbortReceiptStore _receipts;
    private readonly WorkflowLifecycleEventFactory _events;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly WorkflowAccountabilityOutboxAppender _accountabilityOutbox;

    internal WorkflowAbortService(
        IWorkflowInstanceStore workflows,
        IHumanTaskInstanceStore humanTaskStore,
        IHumanTaskRuntime humanTasks,
        IWorkflowStateMachine stateMachine,
        IRuntimeDescriptorPinResolver<WorkflowDescriptor> pinResolver,
        IDescriptorSnapshotStore? snapshots,
        IRuntimeTransactionCoordinator transactions,
        IWorkflowAbortReceiptStore receipts,
        WorkflowLifecycleEventFactory events,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowAccountabilityOutboxAppender accountabilityOutbox)
    {
        _workflows = workflows;
        _humanTaskStore = humanTaskStore;
        _humanTasks = humanTasks;
        _stateMachine = stateMachine;
        _pinResolver = pinResolver;
        _snapshots = snapshots;
        _transactions = transactions;
        _receipts = receipts;
        _events = events;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _accountabilityOutbox = accountabilityOutbox;
    }

    public Task<WorkflowAbortResult> AbortAsync(
        RuntimeInstanceKey workflowKey,
        RuntimeInstanceKey humanTaskKey,
        string reason,
        CancellationToken cancellationToken = default)
        => AbortAsync(workflowKey, humanTaskKey, reason, _events.CreateRunOperationId(), cancellationToken);

    public async Task<WorkflowAbortResult> AbortAsync(
        RuntimeInstanceKey workflowKey,
        RuntimeInstanceKey humanTaskKey,
        string reason,
        string abortOperationId,
        CancellationToken cancellationToken = default)
    {
        workflowKey.EnsureValid();
        humanTaskKey.EnsureValid();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Workflow abort reason is required.", nameof(reason));
        ArgumentException.ThrowIfNullOrWhiteSpace(abortOperationId);
        if (workflowKey.TenantId != humanTaskKey.TenantId)
            throw new InvalidOperationException("Workflow and HumanTask abort keys must share the same tenant scope.");

        WorkflowLifecycleEvent? failedEvent = null;
        WorkflowAbortResult? result = null;
        using var scope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = abortOperationId,
            OperationId = abortOperationId,
            Actor = new AuditActor { Kind = "workflow", Id = workflowKey.InstanceId },
            TenantId = workflowKey.TenantId,
            InvocationSource = "workflow"
        });

        await _transactions.ExecuteAsync(async transactionCt =>
        {
            var tenantScope = new RuntimeTenantScope(workflowKey.TenantId);
            var existing = await _receipts.GetAsync(tenantScope, abortOperationId, transactionCt).ConfigureAwait(false);
            if (existing is not null)
            {
                EnsureSameAbort(existing, workflowKey, humanTaskKey, reason);
                result = new WorkflowAbortResult
                {
                    Status = WorkflowAbortResultStatus.Duplicate,
                    AbortOperationId = abortOperationId
                };
                return;
            }

            var workflow = await _workflows.GetAsync(workflowKey, transactionCt).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Workflow instance '{workflowKey.InstanceId}' was not found.");
            var task = await _humanTaskStore.GetAsync(humanTaskKey, transactionCt).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"HumanTask instance '{humanTaskKey.InstanceId}' was not found.");

            if (workflow.Status != WorkflowInstanceStatus.Suspended
                || workflow.WaitingHumanTaskKey != humanTaskKey
                || task.WorkflowKey != workflowKey)
                throw new InvalidOperationException("Workflow abort requires a reciprocal suspended HumanTask wait.");
            if (task.Status is HumanTaskInstanceStatus.Completed or HumanTaskInstanceStatus.Cancelled)
                throw new InvalidOperationException("Workflow abort cannot consume a terminal HumanTask.");

            await RuntimeDescriptorPinEvidence.ValidateAsync(workflow.WorkflowPin, _snapshots, transactionCt).ConfigureAwait(false);
            await RuntimeDescriptorPinEvidence.ValidateAsync(task.HumanTaskPin, _snapshots, transactionCt).ConfigureAwait(false);
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
                "workflow.failed", candidate, descriptor, identity, abortOperationId,
                fromStatus, abortOperationId, workflow.AuditOrigin?.UpstreamAuditId,
                workflow.LastLifecycleAuditId, WorkflowLifecycleReasonCodes.Aborted,
                workflow.CurrentStepId, humanTaskKey.InstanceId);
            var message = await _accountabilityOutbox.PrepareAsync(failedEvent, transactionCt).ConfigureAwait(false);

            // This is the canonical HumanTask lifecycle authority. Its store
            // update joins the ambient Runtime transaction.
            await _humanTasks.CancelAsync(humanTaskKey, reason, transactionCt).ConfigureAwait(false);
            await _workflows.UpdateAsync(candidate, workflow.Revision, transactionCt).ConfigureAwait(false);

            var receipt = new WorkflowAbortReceipt
            {
                Scope = tenantScope,
                AbortOperationId = abortOperationId,
                WorkflowKey = workflowKey,
                HumanTaskKey = humanTaskKey,
                WorkflowFromRevision = workflow.Revision,
                WorkflowToRevision = workflow.Revision + 1,
                WorkflowPin = workflow.WorkflowPin,
                HumanTaskPin = task.HumanTaskPin,
                Reason = reason,
                Integrity = null!
            };
            receipt = receipt with { Integrity = WorkflowAbortReceiptCanonicalWriter.Compute(receipt) };
            var receiptWrite = await _receipts.AddAsync(receipt, transactionCt).ConfigureAwait(false);
            if (receiptWrite.Status == WorkflowAbortReceiptWriteStatus.Conflict)
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                    "Workflow abort operation id conflicts with a different abort request.");
            if (receiptWrite.Status == WorkflowAbortReceiptWriteStatus.Duplicate)
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                    "Workflow abort operation was concurrently accepted.");

            if (message is not null)
                await _accountabilityOutbox.AppendAsync(message, transactionCt).ConfigureAwait(false);
            result = new WorkflowAbortResult
            {
                Status = WorkflowAbortResultStatus.Accepted,
                AbortOperationId = abortOperationId
            };
        }, cancellationToken).ConfigureAwait(false);

        if (result!.Status == WorkflowAbortResultStatus.Accepted)
            await _eventPublisher.PublishAsync(failedEvent!, CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    private static void EnsureSameAbort(
        WorkflowAbortReceipt existing,
        RuntimeInstanceKey workflowKey,
        RuntimeInstanceKey humanTaskKey,
        string reason)
    {
        if (existing.WorkflowKey != workflowKey
            || existing.HumanTaskKey != humanTaskKey
            || !string.Equals(existing.Reason, reason, StringComparison.Ordinal))
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Workflow abort operation id conflicts with a different abort request.");
        }
    }
}
