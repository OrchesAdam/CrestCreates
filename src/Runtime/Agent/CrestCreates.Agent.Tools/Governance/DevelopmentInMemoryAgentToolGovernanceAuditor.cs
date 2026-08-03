using System.Security.Cryptography;
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
    private readonly Dictionary<AgentToolPreDispatchIdentity, AuditEntry> _entriesByKey = [];
    private readonly Dictionary<string, AuditEntry> _entriesById
        = new(StringComparer.Ordinal);
    private readonly List<AgentToolGovernanceDecisionRecord> _decisions = [];

    public DevelopmentInMemoryAgentToolGovernanceAuditor(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<AgentToolGovernanceDecisionRecord> Decisions
    {
        get
        {
            lock (_sync)
                return _decisions.ToArray();
        }
    }

    public IReadOnlyList<AgentToolGovernanceFinalizationRecord> Finalizations
    {
        get
        {
            lock (_sync)
            {
                return _entriesById.Values
                    .Where(entry => entry.Finalization is not null)
                    .Select(entry => entry.Finalization!)
                    .ToArray();
            }
        }
    }

    public ValueTask RecordDecisionAsync(
        AgentToolGovernanceDecisionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDecision(record);

        lock (_sync)
        {
            if (_decisions.Any(existing =>
                    existing.Context.LogicalInvocationKey == record.Context.LogicalInvocationKey
                    && string.Equals(
                        existing.Context.AttemptId,
                        record.Context.AttemptId,
                        StringComparison.Ordinal)))
            {
                var existing = _decisions.First(item =>
                    item.Context.LogicalInvocationKey == record.Context.LogicalInvocationKey
                    && string.Equals(
                        item.Context.AttemptId,
                        record.Context.AttemptId,
                        StringComparison.Ordinal));
                if (Equivalent(existing, record))
                    return ValueTask.CompletedTask;

                throw new InvalidOperationException(
                    "The governance decision conflicts with the existing AttemptId.");
            }

            _decisions.Add(record);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<AgentToolGovernancePreDispatchWriteResult> RecordPreDispatchAsync(
        AgentToolGovernancePreDispatchRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePreDispatch(record);

        lock (_sync)
        {
            var identity = new AgentToolPreDispatchIdentity(
                record.Context.LogicalInvocationKey,
                record.Context.AttemptId);
            if (_entriesByKey.TryGetValue(identity, out var existing))
            {
                if (!Equivalent(existing.PreDispatch, record))
                {
                    return ValueTask.FromResult(new AgentToolGovernancePreDispatchWriteResult
                    {
                        Status = AgentToolGovernancePreDispatchWriteStatus.Conflict
                    });
                }

                return ValueTask.FromResult(new AgentToolGovernancePreDispatchWriteResult
                {
                    Status = AgentToolGovernancePreDispatchWriteStatus.Duplicate,
                    Receipt = existing.Receipt
                });
            }

            var receipt = new AgentToolGovernancePreDispatchReceipt
            {
                Identity = identity,
                AuditId = $"audit-{Guid.NewGuid():N}",
                AcceptedAt = _timeProvider.GetUtcNow()
            };
            var entry = new AuditEntry(receipt, record);
            _entriesByKey.Add(identity, entry);
            _entriesById.Add(receipt.AuditId, entry);
            return ValueTask.FromResult(new AgentToolGovernancePreDispatchWriteResult
            {
                Status = AgentToolGovernancePreDispatchWriteStatus.Accepted,
                Receipt = receipt
            });
        }
    }

    public ValueTask<AgentToolGovernancePreDispatchReadResult> GetPreDispatchStateAsync(
        AgentToolPreDispatchIdentity identity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_entriesByKey.TryGetValue(identity, out var existing))
            {
                return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
                {
                    Status = AgentToolGovernancePreDispatchReadStatus.Missing
                });
            }

            return ValueTask.FromResult(new AgentToolGovernancePreDispatchReadResult
            {
                Status = AgentToolGovernancePreDispatchReadStatus.Accepted,
                Receipt = existing.Receipt,
                Checkpoint = DeepClone(existing.PreDispatch)
            });
        }
    }

    public ValueTask<AgentToolGovernanceFinalizationResult> FinalizeAsync(
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
                    return ValueTask.FromResult(Finalized(entry.Finalization));
                }

                throw new InvalidOperationException(
                    "A finalized governance AuditId cannot be changed.");
            }

            entry.Finalization = record;
            return ValueTask.FromResult(Finalized(record));
        }
    }

    public ValueTask<AgentToolGovernanceFinalizationResult> GetFinalizationStateAsync(
        string auditId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_entriesById.TryGetValue(auditId, out var entry))
            {
                return ValueTask.FromResult(new AgentToolGovernanceFinalizationResult
                {
                    Status = AgentToolGovernanceFinalizationStatus.Unknown
                });
            }

            return ValueTask.FromResult(entry.Finalization is null
                ? new AgentToolGovernanceFinalizationResult
                {
                    Status = AgentToolGovernanceFinalizationStatus.NotFinalized
                }
                : Finalized(entry.Finalization));
        }
    }

    private static AgentToolGovernanceFinalizationResult Finalized(
        AgentToolGovernanceFinalizationRecord record)
        => new()
        {
            Status = AgentToolGovernanceFinalizationStatus.Finalized,
            Record = record
        };

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

    private static void ValidateDecision(AgentToolGovernanceDecisionRecord record)
    {
        if (!IsValid(record.Context)
            || record.Decision is not (
                AgentToolGovernanceDecisionState.Denied
                or AgentToolGovernanceDecisionState.Indeterminate)
            || record.Outcome is null
            || string.IsNullOrWhiteSpace(record.Outcome.Code)
            || string.IsNullOrWhiteSpace(record.ReasonCode)
            || record.Outcome.Kind == AgentToolInvocationOutcomeKind.Unknown
            || !IsDecisionConsistent(record))
        {
            throw new ArgumentException(
                "Governance decision record has an invalid contract.",
                nameof(record));
        }
    }

    private static bool IsDecisionConsistent(AgentToolGovernanceDecisionRecord record)
        => record.Decision switch
        {
            AgentToolGovernanceDecisionState.Denied => record.Outcome.Kind is
                AgentToolInvocationOutcomeKind.UnknownTool
                or AgentToolInvocationOutcomeKind.InvalidRequest
                or AgentToolInvocationOutcomeKind.GovernanceDenied,
            AgentToolGovernanceDecisionState.Indeterminate =>
                record.Outcome.Kind == AgentToolInvocationOutcomeKind.InvocationIndeterminate,
            _ => false
        };

    private static bool Equivalent(
        AgentToolGovernanceDecisionRecord left,
        AgentToolGovernanceDecisionRecord right)
        => left.Context.LogicalInvocationKey == right.Context.LogicalInvocationKey
            && string.Equals(left.Context.AttemptId, right.Context.AttemptId, StringComparison.Ordinal)
            && left.Context.Equals(right.Context)
            && string.Equals(left.Context.InvocationFingerprint, right.Context.InvocationFingerprint, StringComparison.Ordinal)
            && left.Decision == right.Decision
            && left.Outcome.Kind == right.Outcome.Kind
            && string.Equals(left.Outcome.Code, right.Outcome.Code, StringComparison.Ordinal)
            && string.Equals(left.Outcome.Message, right.Outcome.Message, StringComparison.Ordinal)
            && left.Outcome.Issues.SequenceEqual(right.Outcome.Issues)
            && string.Equals(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal)
            && Equals(left.ObservedReservation, right.ObservedReservation);

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
            || record.BudgetReservation.State == AgentToolBudgetReservationState.Reserved
            || record.BudgetReservation.State is not (
                AgentToolBudgetReservationState.Unknown
                or
                AgentToolBudgetReservationState.Released
                or AgentToolBudgetReservationState.Committed
                or AgentToolBudgetReservationState.Indeterminate)
            || !Matches(record.BudgetReservation, record.Context)
            || record.AttemptState is not (
                AgentToolGovernanceAttemptFinalState.Released
                or AgentToolGovernanceAttemptFinalState.Completed
                or AgentToolGovernanceAttemptFinalState.Indeterminate)
            || record.Outcome is null
            || !HasValidOutcomeHash(record)
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
            && (context.ArgumentsEvaluated
                ? !string.IsNullOrWhiteSpace(context.ArgumentsHash)
                : context.ArgumentsHash is null)
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
        => AgentToolGovernancePreDispatchComparer.Equivalent(left, right);

    private static bool MatchesPreDispatch(
        AgentToolGovernancePreDispatchRecord preDispatch,
        AgentToolGovernanceFinalizationRecord finalization)
        => EquivalentContext(preDispatch.Context, finalization.Context)
            && preDispatch.Lease.Equals(finalization.Lease)
            && MatchesReservationIdentity(
                preDispatch.BudgetReservation,
                finalization.BudgetReservation);

    private static bool MatchesReservationIdentity(
        AgentToolBudgetReservation left,
        AgentToolBudgetReservation right)
        => string.Equals(left.ReservationId, right.ReservationId, StringComparison.Ordinal)
            && string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal)
            && string.Equals(
                left.InvocationFingerprint,
                right.InvocationFingerprint,
                StringComparison.Ordinal)
            && string.Equals(left.Category, right.Category, StringComparison.Ordinal)
            && left.CostUnits == right.CostUnits
            && left.MaxCallsPerExecution == right.MaxCallsPerExecution;

    private static bool Equivalent(
        AgentToolGovernanceFinalizationRecord left,
        AgentToolGovernanceFinalizationRecord right)
        => string.Equals(left.AuditId, right.AuditId, StringComparison.Ordinal)
            && EquivalentContext(left.Context, right.Context)
            && left.Lease.Equals(right.Lease)
            && left.BudgetReservation.Equals(right.BudgetReservation)
            && left.DispatchStarted == right.DispatchStarted
            && left.AttemptState == right.AttemptState
            && left.InvocationState == right.InvocationState
            && string.Equals(
                left.OutcomeHash ?? AgentToolGovernanceOutcomeHasher.Compute(left.Outcome, left.AuditFacts),
                right.OutcomeHash ?? AgentToolGovernanceOutcomeHasher.Compute(right.Outcome, right.AuditFacts),
                StringComparison.Ordinal)
            && left.AuditFacts.SequenceEqual(right.AuditFacts)
            && string.Equals(left.ReasonCode, right.ReasonCode, StringComparison.Ordinal);

    private static bool EquivalentContext(
        AgentToolGovernanceAuditContext left,
        AgentToolGovernanceAuditContext right)
        => left.LogicalInvocationKey == right.LogicalInvocationKey
            && string.Equals(left.AttemptId, right.AttemptId, StringComparison.Ordinal)
            && string.Equals(left.InvocationFingerprint, right.InvocationFingerprint, StringComparison.Ordinal)
            && string.Equals(left.ArgumentsHash, right.ArgumentsHash, StringComparison.Ordinal)
            && left.ArgumentsEvaluated == right.ArgumentsEvaluated
            && left.CallOrigin == right.CallOrigin
            && string.Equals(left.AgentRolesHash, right.AgentRolesHash, StringComparison.Ordinal)
            && left.ToolContract.Equals(right.ToolContract)
            && left.CapabilityContract.Equals(right.CapabilityContract)
            && Equals(left.InputSchemaContract, right.InputSchemaContract)
            && Equals(left.OutputSchemaContract, right.OutputSchemaContract)
            && left.Governance.Equals(right.Governance);

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

    private static bool HasValidOutcomeHash(AgentToolGovernanceFinalizationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.OutcomeHash)
            || record.Outcome.Issues is null
            || record.Outcome.Issues.Any(issue => issue is null))
            return false;

        try
        {
            var supplied = Convert.FromHexString(record.OutcomeHash);
            var computed = Convert.FromHexString(
                AgentToolGovernanceOutcomeHasher.Compute(record.Outcome, record.AuditFacts));
            return CryptographicOperations.FixedTimeEquals(supplied, computed);
        }
        catch (FormatException)
        {
            return false;
        }
    }

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
                    && record.BudgetReservation.State is (
                        AgentToolBudgetReservationState.Committed
                        or AgentToolBudgetReservationState.Indeterminate
                        or AgentToolBudgetReservationState.Unknown)
                    || !record.DispatchStarted
                    && record.BudgetReservation.State is (
                        AgentToolBudgetReservationState.Released
                        or AgentToolBudgetReservationState.Unknown))
                && record.InvocationState is null or AgentToolInvocationTerminalState.Indeterminate
                && record.Outcome.Kind == AgentToolInvocationOutcomeKind.InvocationIndeterminate,
            _ => false
        };

    private static AgentToolGovernancePreDispatchRecord DeepClone(
        AgentToolGovernancePreDispatchRecord source)
    {
        var context = source.Context with
        {
            Governance = source.Context.Governance with
            {
                Budget = source.Context.Governance.Budget with { }
            }
        };
        return source with
        {
            Context = context,
            Lease = source.Lease with { },
            Approval = source.Approval with { },
            BudgetReservation = source.BudgetReservation with { }
        };
    }

    private sealed class AuditEntry(
        AgentToolGovernancePreDispatchReceipt receipt,
        AgentToolGovernancePreDispatchRecord preDispatch)
    {
        public AgentToolGovernancePreDispatchReceipt Receipt { get; } = receipt;

        public AgentToolGovernancePreDispatchRecord PreDispatch { get; } = preDispatch;

        public AgentToolGovernanceFinalizationRecord? Finalization { get; set; }
    }
}
