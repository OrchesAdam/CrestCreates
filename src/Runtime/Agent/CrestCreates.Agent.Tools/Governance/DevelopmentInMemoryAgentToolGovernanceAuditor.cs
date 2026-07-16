using CrestCreates.Agent.Abstractions;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Volatile two-checkpoint governance auditor intended only for development and
/// tests. Selecting this type explicitly permits Required audit records to be
/// acknowledged in one process, but records are lost on restart and are not
/// coordinated across nodes.
/// </summary>
public sealed class DevelopmentInMemoryAgentToolGovernanceAuditor
    : IAgentToolGovernanceAuditor
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<AuditKey, AuditEntry> _entriesByKey = [];
    private readonly Dictionary<string, AuditEntry> _entriesById
        = new(StringComparer.Ordinal);

    public DevelopmentInMemoryAgentToolGovernanceAuditor(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<AgentToolGovernanceAuditHandle> RecordPreDispatchAsync(
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePreDispatch(record);

        lock (_sync)
        {
            var key = new AuditKey(
                record.Context.LogicalInvocationKey,
                record.Context.AttemptId);
            if (_entriesByKey.TryGetValue(key, out var existing))
            {
                if (!Equivalent(existing.PreDispatch, record))
                {
                    throw new InvalidOperationException(
                        "The governance pre-dispatch checkpoint conflicts with the existing AuditId.");
                }

                return ValueTask.FromResult(existing.Handle);
            }

            var handle = new AgentToolGovernanceAuditHandle
            {
                AuditId = $"audit-{Guid.NewGuid():N}",
                AcceptedAt = _timeProvider.GetUtcNow()
            };
            var entry = new AuditEntry(handle, record);
            _entriesByKey.Add(key, entry);
            _entriesById.Add(handle.AuditId, entry);
            return ValueTask.FromResult(handle);
        }
    }

    public ValueTask FinalizeAsync(
        AgentToolGovernanceFinalizationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateFinalization(record);

        lock (_sync)
        {
            if (!_entriesById.TryGetValue(record.AuditId, out var entry))
            {
                throw new InvalidOperationException("The governance AuditId does not exist.");
            }

            if (!MatchesPreDispatch(entry.PreDispatch, record))
            {
                throw new InvalidOperationException(
                    "The governance finalization does not match its pre-dispatch checkpoint.");
            }

            if (entry.Finalization is not null)
            {
                if (Equivalent(entry.Finalization, record))
                {
                    return ValueTask.CompletedTask;
                }

                throw new InvalidOperationException(
                    "A finalized governance AuditId cannot be changed.");
            }

            entry.Finalization = record;
            return ValueTask.CompletedTask;
        }
    }

    private static void ValidatePreDispatch(AgentToolGovernancePreDispatchRecord record)
    {
        var context = record.Context;
        if (!IsValid(context)
            || context.Governance.EffectiveAuditMode is not (
                AgentToolAuditMode.Required or AgentToolAuditMode.BestEffort)
            || record.Lease is null
            || string.IsNullOrWhiteSpace(record.Lease.AttemptId)
            || string.IsNullOrWhiteSpace(record.Lease.LeaseId)
            || record.Lease.FencingToken <= 0
            || record.Lease.ExpiresAt <= record.Lease.AcquiredAt
            || !string.Equals(record.Lease.AttemptId, context.AttemptId, StringComparison.Ordinal)
            || record.Approval is null
            || !IsAcceptedApproval(record.Approval)
            || record.BudgetReservation is null
            || record.BudgetReservation.State != AgentToolBudgetReservationState.Reserved
            || !Matches(record.BudgetReservation, context))
        {
            throw new ArgumentException(
                "Governance pre-dispatch record has an invalid contract.",
                nameof(record));
        }
    }

    private static void ValidateFinalization(AgentToolGovernanceFinalizationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.AuditId)
            || !IsValid(record.Context)
            || record.Lease is null
            || !string.Equals(
                record.Lease.AttemptId,
                record.Context.AttemptId,
                StringComparison.Ordinal)
            || record.BudgetReservation is null
            || record.BudgetReservation.State is AgentToolBudgetReservationState.Unknown
                or AgentToolBudgetReservationState.Reserved
            || record.BudgetReservation.State is not (
                AgentToolBudgetReservationState.Released
                or AgentToolBudgetReservationState.Committed
                or AgentToolBudgetReservationState.Indeterminate)
            || !Matches(record.BudgetReservation, record.Context)
            || record.AttemptState is not (
                AgentToolGovernanceAttemptFinalState.Released
                or AgentToolGovernanceAttemptFinalState.Completed
                or AgentToolGovernanceAttemptFinalState.Indeterminate)
            || record.Outcome is null
            || record.Outcome.Kind == AgentToolInvocationOutcomeKind.Unknown
            || !IsKnown(record.Outcome.Kind)
            || !IsConsistent(record)
            || string.IsNullOrWhiteSpace(record.Outcome.Code)
            || string.IsNullOrWhiteSpace(record.ReasonCode))
        {
            throw new ArgumentException(
                "Governance finalization record has an invalid contract.",
                nameof(record));
        }
    }

    private static bool IsValid(AgentToolGovernanceAuditContext context)
        => context is not null
            && AgentToolGovernanceGuard.IsValid(context.LogicalInvocationKey)
            && !string.IsNullOrWhiteSpace(context.AttemptId)
            && !string.IsNullOrWhiteSpace(context.InvocationFingerprint)
            && !string.IsNullOrWhiteSpace(context.ArgumentsHash)
            && context.CallOrigin is AgentToolCallOrigin.ExplicitRequest
                or AgentToolCallOrigin.AutomaticSelection
            && !string.IsNullOrWhiteSpace(context.AgentRolesHash)
            && AgentToolGovernanceGuard.IsValid(context.ToolContract)
            && AgentToolGovernanceGuard.IsValid(context.CapabilityContract)
            && AgentToolGovernanceGuard.IsValid(context.InputSchemaContract)
            && AgentToolGovernanceGuard.IsValid(context.OutputSchemaContract)
            && AgentToolGovernanceGuard.IsValid(context.Governance);

    private static bool Matches(
        AgentToolBudgetReservation reservation,
        AgentToolGovernanceAuditContext context)
        => string.Equals(reservation.AttemptId, context.AttemptId, StringComparison.Ordinal)
            && string.Equals(
                reservation.InvocationFingerprint,
                context.InvocationFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                reservation.Category,
                context.Governance.Budget.Category,
                StringComparison.Ordinal)
            && reservation.CostUnits == context.Governance.Budget.CostUnits
            && reservation.MaxCallsPerExecution
                == context.Governance.Budget.MaxCallsPerExecution;

    private static bool Equivalent(
        AgentToolGovernancePreDispatchRecord left,
        AgentToolGovernancePreDispatchRecord right)
        => left.Context.LogicalInvocationKey == right.Context.LogicalInvocationKey
            && string.Equals(left.Context.AttemptId, right.Context.AttemptId, StringComparison.Ordinal)
            && string.Equals(
                left.Context.InvocationFingerprint,
                right.Context.InvocationFingerprint,
                StringComparison.Ordinal)
            && string.Equals(left.Lease.LeaseId, right.Lease.LeaseId, StringComparison.Ordinal)
            && left.Lease.FencingToken == right.Lease.FencingToken
            && string.Equals(
                left.BudgetReservation.ReservationId,
                right.BudgetReservation.ReservationId,
                StringComparison.Ordinal)
            && left.Approval.Decision == right.Approval.Decision
            && left.Approval.ClaimState == right.Approval.ClaimState
            && string.Equals(left.Approval.EvidenceId, right.Approval.EvidenceId, StringComparison.Ordinal);

    private static bool MatchesPreDispatch(
        AgentToolGovernancePreDispatchRecord preDispatch,
        AgentToolGovernanceFinalizationRecord finalization)
        => preDispatch.Context.LogicalInvocationKey == finalization.Context.LogicalInvocationKey
            && string.Equals(
                preDispatch.Context.AttemptId,
                finalization.Context.AttemptId,
                StringComparison.Ordinal)
            && string.Equals(
                preDispatch.Context.InvocationFingerprint,
                finalization.Context.InvocationFingerprint,
                StringComparison.Ordinal)
            && string.Equals(
                preDispatch.Lease.LeaseId,
                finalization.Lease.LeaseId,
                StringComparison.Ordinal)
            && preDispatch.Lease.FencingToken == finalization.Lease.FencingToken
            && string.Equals(
                preDispatch.BudgetReservation.ReservationId,
                finalization.BudgetReservation.ReservationId,
                StringComparison.Ordinal);

    private static bool Equivalent(
        AgentToolGovernanceFinalizationRecord left,
        AgentToolGovernanceFinalizationRecord right)
        => string.Equals(left.AuditId, right.AuditId, StringComparison.Ordinal)
            && MatchesPreDispatch(
                new AgentToolGovernancePreDispatchRecord
                {
                    Context = left.Context,
                    Lease = left.Lease,
                    Approval = new AgentToolApprovalResult
                    {
                        Decision = AgentToolApprovalDecision.NotRequired,
                        ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable
                    },
                    BudgetReservation = left.BudgetReservation
                },
                right)
            && left.DispatchStarted == right.DispatchStarted
            && left.BudgetReservation.State == right.BudgetReservation.State
            && left.AttemptState == right.AttemptState
            && left.InvocationState == right.InvocationState
            && left.Outcome.Kind == right.Outcome.Kind
            && string.Equals(left.Outcome.Code, right.Outcome.Code, StringComparison.Ordinal)
            && string.Equals(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal);

    private static bool IsKnown(AgentToolInvocationOutcomeKind kind)
        => kind is AgentToolInvocationOutcomeKind.Succeeded
            or AgentToolInvocationOutcomeKind.UnknownTool
            or AgentToolInvocationOutcomeKind.InvalidRequest
            or AgentToolInvocationOutcomeKind.GovernanceDenied
            or AgentToolInvocationOutcomeKind.InProgress
            or AgentToolInvocationOutcomeKind.InvocationConflict
            or AgentToolInvocationOutcomeKind.InvocationIndeterminate
            or AgentToolInvocationOutcomeKind.CapabilityFailure
            or AgentToolInvocationOutcomeKind.InternalContractFailure
            or AgentToolInvocationOutcomeKind.InternalServer;

    private static bool IsAcceptedApproval(AgentToolApprovalResult approval)
        => approval.Decision switch
        {
            AgentToolApprovalDecision.Approved =>
                approval.ClaimState == AgentToolApprovalEvidenceClaimState.Claimed
                && !string.IsNullOrWhiteSpace(approval.EvidenceId),
            AgentToolApprovalDecision.NotRequired =>
                approval.ClaimState == AgentToolApprovalEvidenceClaimState.NotApplicable
                && string.IsNullOrWhiteSpace(approval.EvidenceId),
            _ => false
        };

    private static bool IsConsistent(AgentToolGovernanceFinalizationRecord record)
        => record.AttemptState switch
        {
            AgentToolGovernanceAttemptFinalState.Released =>
                !record.DispatchStarted
                && record.BudgetReservation.State == AgentToolBudgetReservationState.Released
                && record.InvocationState is null
                && record.Outcome.Kind is AgentToolInvocationOutcomeKind.GovernanceDenied
                    or AgentToolInvocationOutcomeKind.InProgress
                    or AgentToolInvocationOutcomeKind.InvocationConflict
                    or AgentToolInvocationOutcomeKind.InternalServer,
            AgentToolGovernanceAttemptFinalState.Completed =>
                record.DispatchStarted
                && record.BudgetReservation.State is AgentToolBudgetReservationState.Released
                    or AgentToolBudgetReservationState.Committed
                && record.InvocationState == AgentToolInvocationTerminalState.Completed
                && record.Outcome.Kind is AgentToolInvocationOutcomeKind.Succeeded
                    or AgentToolInvocationOutcomeKind.CapabilityFailure
                    or AgentToolInvocationOutcomeKind.InternalContractFailure,
            AgentToolGovernanceAttemptFinalState.Indeterminate =>
                (record.DispatchStarted
                    && record.BudgetReservation.State is AgentToolBudgetReservationState.Committed
                        or AgentToolBudgetReservationState.Indeterminate
                    || !record.DispatchStarted
                    && record.BudgetReservation.State == AgentToolBudgetReservationState.Released)
                && record.InvocationState == AgentToolInvocationTerminalState.Indeterminate
                && record.Outcome.Kind == AgentToolInvocationOutcomeKind.InvocationIndeterminate,
            _ => false
        };

    private sealed class AuditEntry(
        AgentToolGovernanceAuditHandle handle,
        AgentToolGovernancePreDispatchRecord preDispatch)
    {
        public AgentToolGovernanceAuditHandle Handle { get; } = handle;

        public AgentToolGovernancePreDispatchRecord PreDispatch { get; } = preDispatch;

        public AgentToolGovernanceFinalizationRecord? Finalization { get; set; }
    }

    private readonly record struct AuditKey(
        AgentToolLogicalInvocationKey LogicalInvocationKey,
        string AttemptId);
}
