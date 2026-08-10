using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Semantics;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// No-op reconciliation accountability producer. Default until the real
/// producer is activated, keeping reconciliation result writing independent
/// of accountability availability.
/// </summary>
public sealed class NullAgentToolPreDispatchReconciliationAccountabilityProducer
    : IAgentToolPreDispatchReconciliationAccountabilityProducer
{
    public ValueTask PublishAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}

/// <summary>
/// Real Accountability producer (activated in Slice 6). Uses IAuditRecorder,
/// never IAuditSink. Emits only safe IDs/descriptors/reason families.
/// Accountability failure is observed/logged and cannot alter the reconciliation result.
/// </summary>
public sealed class AgentToolPreDispatchReconciliationAccountabilityProducer
    : IAgentToolPreDispatchReconciliationAccountabilityProducer
{
    private readonly IAuditRecorder _auditRecorder;
    private readonly TimeProvider _timeProvider;

    public AgentToolPreDispatchReconciliationAccountabilityProducer(
        IAuditRecorder auditRecorder,
        TimeProvider? timeProvider = null)
    {
        _auditRecorder = auditRecorder ?? throw new ArgumentNullException(nameof(auditRecorder));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask PublishAsync(
        AgentToolPreDispatchIdentity identity,
        AgentToolPreDispatchReconciliationStatus status,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var now = _timeProvider.GetUtcNow();
            var auditId = $"acr-{identity.LogicalInvocationKey.InvocationId}-{identity.AttemptId}-{now:yyyyMMddHHmmssfff}";

            var envelope = new AuditEnvelope
            {
                AuditId = auditId,
                OccurredAt = now,
                TenantId = identity.LogicalInvocationKey.TenantId,
                CorrelationId = identity.LogicalInvocationKey.ExecutionId,
                CausationId = identity.LogicalInvocationKey.InvocationId,
                Actor = new AuditActor
                {
                    Kind = AuditActorKinds.System,
                    Id = "agent-tool-reconciler",
                    DisplayName = "Agent Tool Pre-Dispatch Reconciler"
                },
                Action = new AuditAction
                {
                    Kind = "control.transition",
                    Name = "AgentToolPreDispatchReconciliation"
                },
                Target = new AuditTarget
                {
                    Kind = "agent-tool-invocation",
                    Id = $"{identity.LogicalInvocationKey.InvocationId}:{identity.AttemptId}"
                },
                Outcome = new AuditOutcome
                {
                    // P1-05: map reconciliation outcomes to accountability semantics.
                    // Released → succeeded; Conflict → rejected; PostDispatchUnknown and
                    // StillPending → indeterminate (the attempt did not fail, but its
                    // terminal disposition is not confirmable).
                    Status = status switch
                    {
                        AgentToolPreDispatchReconciliationStatus.Released => AuditOutcomeStatuses.Succeeded,
                        AgentToolPreDispatchReconciliationStatus.Conflict => AuditOutcomeStatuses.Rejected,
                        AgentToolPreDispatchReconciliationStatus.PostDispatchUnknown => AuditOutcomeStatuses.Indeterminate,
                        _ => AuditOutcomeStatuses.Indeterminate
                    },
                    Code = reasonCode
                },
                Tags = AuditTagMap.Empty.Add("reconciliation.status", status.ToString())
            };

            await _auditRecorder.RecordAsync(envelope, cancellationToken);
        }
        catch
        {
            // Accountability failure is observed/logged and cannot alter the reconciliation result.
            // The control terminal/receipt is already persisted; projection may retry independently.
        }
    }
}
