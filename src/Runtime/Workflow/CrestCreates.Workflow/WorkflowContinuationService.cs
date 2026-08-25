using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Abstractions.Delivery;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Workflow.Accountability;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Accountability.Abstractions.Json;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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
    private readonly IWorkflowContinuationAcceptanceStore? _acceptances;
    private readonly IRuntimeTransactionCoordinator? _transactions;
    private readonly IAuditEnvelopePreparer? _auditPreparer;
    private readonly ITransactionalOutboxWriter? _outboxWriter;
    private readonly IOutboxMessageFactory? _outboxFactory;
    private readonly ILogger<WorkflowContinuationService>? _logger;

    public WorkflowContinuationService(
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowLifecycleEventFactory events,
        IRuntimeStateContractRegistry stateRegistry,
        IRuntimeDescriptorPinResolver<WorkflowDescriptor> pinResolver,
        IDescriptorSnapshotStore? snapshots,
        IWorkflowContinuationAcceptanceStore? acceptances = null,
        IRuntimeTransactionCoordinator? transactions = null,
        IAuditEnvelopePreparer? auditPreparer = null,
        ITransactionalOutboxWriter? outboxWriter = null,
        IOutboxMessageFactory? outboxFactory = null,
        ILogger<WorkflowContinuationService>? logger = null)
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
        _acceptances = acceptances;
        _transactions = transactions;
        _auditPreparer = auditPreparer;
        _outboxWriter = outboxWriter;
        _outboxFactory = outboxFactory;
        _logger = logger;
    }

    public async Task ContinueAsync(
        WorkflowContinuationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CompletionEventId))
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Workflow continuation requires the persisted HumanTask CompletionEventId.");

        // The acceptance receipt is the authority for an already accepted
        // completion.  It must be checked before the waiting-key lookup because
        // a successful continuation clears that key; absence is not evidence of
        // a duplicate and must never be treated as one.
        if (await TryReturnForExistingAcceptanceAsync(request, ct).ConfigureAwait(false))
            return;

        var instance = await _store.GetByWaitingHumanTaskAsync(request.HumanTaskKey, ct)
            .ConfigureAwait(false);
        if (instance == null)
            throw MissingContinuationProof(request);

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

        var resumedEvent = _events.Create(
            "workflow.resumed", candidate, descriptor, resumedIdentity, runOperationId,
            resumedFromStatus, request.TriggerOperationId ?? request.CompletionEventId,
            request.TriggerAuditId, resumedPreviousId,
            humanTaskInstanceId: request.HumanTaskKey.InstanceId,
            humanTaskCompletionEventId: request.CompletionEventId);
        OutboxMessage? accountabilityMessage = null;
        if (_auditPreparer is not null && _outboxWriter is not null && _outboxFactory is not null)
        {
            var prepared = await _auditPreparer.PrepareAsync(WorkflowAccountabilityEnvelopeFactory.Create(resumedEvent), ct).ConfigureAwait(false);
            if (!prepared.IsAccepted || prepared.Envelope is null)
                throw new RuntimePersistenceContractException(
                    RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                    $"Workflow Accountability envelope preparation was rejected: {string.Join(", ", prepared.Issues.Select(issue => $"{issue.Code}|{issue.Path ?? "<none>"}"))}.");
            accountabilityMessage = _outboxFactory.Create(
                new OutboxMessageMetadata
                {
                    MessageId = prepared.Envelope.AuditId,
                    TenantId = prepared.Envelope.TenantId,
                    ContractId = "crest.accountability.audit-envelope/v1",
                    PayloadTypeId = "CrestCreates.Accountability.AuditEnvelope/v1",
                    EventName = resumedEvent.EventType,
                    EventVersion = 1,
                    CorrelationId = prepared.Envelope.CorrelationId,
                    CausationId = prepared.Envelope.CausationId,
                    OccurredAt = prepared.Envelope.OccurredAt,
                    RequiredConsumerIds = [],
                    CreatedAt = prepared.Envelope.OccurredAt
                },
                prepared.Envelope,
                AccountabilityJsonSerializerContext.Default.AuditEnvelope);
        }

        var expectedRevision = instance.Revision;
        var acceptance = _acceptances is null ? null : new WorkflowContinuationAcceptance
        {
            TenantScope = new CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope(instance.TenantId),
            CompletionEventId = request.CompletionEventId!,
            HumanTaskKey = request.HumanTaskKey,
            WorkflowKey = instance.Key,
            Outcome = request.Outcome,
            Result = request.Result,
            WorkflowFromRevision = expectedRevision,
            WorkflowToRevision = expectedRevision + 1,
            Integrity = WorkflowContinuationAcceptanceCanonicalWriter.Compute(new WorkflowContinuationAcceptance
            {
                TenantScope = new CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope(instance.TenantId),
                CompletionEventId = request.CompletionEventId!,
                HumanTaskKey = request.HumanTaskKey,
                WorkflowKey = instance.Key,
                Outcome = request.Outcome,
                Result = request.Result,
                WorkflowFromRevision = expectedRevision,
                WorkflowToRevision = expectedRevision + 1,
                Integrity = new CanonicalHash
                {
                    Value = string.Empty, Algorithm = "", AlgorithmVersion = "", ArtifactKind = "", Scope = "", Purpose = "", ContractVersion = "", CanonicalShapeVersion = ""
                }
            })
        };
        try
        {
            async ValueTask PersistAsync(CancellationToken token)
            {
                await _store.UpdateAsync(candidate, expectedRevision, token).ConfigureAwait(false);
                candidate.Revision = expectedRevision + 1;
                if (acceptance is not null)
                {
                    var result = await _acceptances!.AddAsync(acceptance, token).ConfigureAwait(false);
                    if (result == WorkflowContinuationAcceptanceWriteResult.Conflict)
                        throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict, "Workflow continuation acceptance conflicts with a durable decision.");
                }
                if (accountabilityMessage is not null)
                {
                    var append = await _outboxWriter!.AppendAsync(accountabilityMessage, token).ConfigureAwait(false);
                    if (append is not (OutboxAppendResult.Appended or OutboxAppendResult.Duplicate))
                        throw new InvalidOperationException("Workflow Accountability Outbox append was not accepted.");
                }
            }
            if (_transactions is null) await PersistAsync(ct).ConfigureAwait(false);
            else await _transactions.ExecuteAsync(PersistAsync, ct).ConfigureAwait(false);
        }
        catch (RuntimeConcurrencyException)
        {
            // Another continuation may have committed before the response was
            // observed. Reconcile only through the durable acceptance
            // discriminator; waiting-key absence alone is not proof.
            if (await TryReturnForExistingAcceptanceAsync(request, ct).ConfigureAwait(false))
                return;

            // A still-present waiting correlation proves that this was an
            // unrelated concurrent write, so preserve the original conflict.
            var recheck = await _store.GetByWaitingHumanTaskAsync(request.HumanTaskKey, ct)
                .ConfigureAwait(false);
            if (recheck is not null)
                throw;

            // The durable winner is not this CompletionEventId (or the
            // acceptance store is unavailable), therefore fail closed instead
            // of acknowledging a message without proof of acceptance.
            throw MissingContinuationProof(request);
        }

        await _eventPublisher.PublishAsync(resumedEvent, CancellationToken.None).ConfigureAwait(false);

        try
        {
            await _executionRunner.RunAsync(candidate, runOperationId, parent?.EnclosingAuditId, ct).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The acceptance is the durable Ack boundary.  Post-resume execution
            // is intentionally not retried by the completion message, but the
            // failure must remain observable for operational reconciliation.
            _logger?.LogError(exception,
                "Workflow continuation accepted but post-resume execution failed for {WorkflowInstanceId}.",
                candidate.InstanceId);
        }
    }

    private async Task<bool> TryReturnForExistingAcceptanceAsync(
        WorkflowContinuationRequest request,
        CancellationToken cancellationToken)
    {
        if (_acceptances is null || string.IsNullOrWhiteSpace(request.CompletionEventId))
            return false;

        var existing = await _acceptances.GetAsync(
            new CrestCreates.Runtime.Persistence.Abstractions.Keys.RuntimeTenantScope(request.WorkflowKey.TenantId),
            request.CompletionEventId!, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        EnsureAcceptanceMatches(existing, request);
        return true;
    }

    private static RuntimePersistenceContractException MissingContinuationProof(
        WorkflowContinuationRequest request)
        => new(
            RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
            $"Workflow continuation has neither a waiting correlation nor an exact durable acceptance for '{request.CompletionEventId}'.");

    private static bool ResultsEqual(RuntimeStateValue? left, RuntimeStateValue? right)
        => left is null && right is null ||
           left is not null && right is not null &&
           string.Equals(left.TypeId, right.TypeId, StringComparison.Ordinal) &&
           string.Equals(left.SchemaRef?.ToString(), right.SchemaRef?.ToString(), StringComparison.Ordinal) &&
           string.Equals(left.JsonPayload, right.JsonPayload, StringComparison.Ordinal);

    private static void EnsureAcceptanceMatches(
        WorkflowContinuationAcceptance existing,
        WorkflowContinuationRequest request)
    {
        var recomputed = WorkflowContinuationAcceptanceCanonicalWriter.Compute(existing);
        if (!string.Equals(existing.Integrity.Value, recomputed.Value, StringComparison.Ordinal))
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.PersistedInvariantViolation,
                "Workflow continuation acceptance integrity does not match its durable receipt.");
        if (existing.HumanTaskKey != request.HumanTaskKey
            || existing.WorkflowKey != request.WorkflowKey
            || !string.Equals(existing.Outcome, request.Outcome, StringComparison.Ordinal)
            || !ResultsEqual(existing.Result, request.Result))
        {
            throw new RuntimePersistenceContractException(
                RuntimePersistenceContractErrorCode.WaitingTaskCorrelationConflict,
                "Workflow continuation event id is already bound to different durable facts.");
        }
    }
}
