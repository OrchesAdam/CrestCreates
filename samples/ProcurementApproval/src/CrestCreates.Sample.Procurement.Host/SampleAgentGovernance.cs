using System.Collections.Concurrent;
using CrestCreates.Agent.Tools;

namespace CrestCreates.Sample.Procurement.Host;

public sealed class SampleAgentToolApprovalGate : IAgentToolApprovalGate
{
    public const string ApprovedEvidence = "sample-approved";

    public ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(
        AgentToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var required = request.Context.Governance.EffectiveApprovalMode
                == CrestCreates.Metadata.AgentTool.AgentToolApprovalMode.Required
            || request.Context.Governance.EffectiveRisk is
                CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityRiskLevel.High or
                CrestCreates.Metadata.Abstractions.DescriptorCapability.CapabilityRiskLevel.Critical;

        if (!required)
        {
            return ValueTask.FromResult(new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable,
                ReasonCode = "approval_not_required"
            });
        }

        var approved = string.Equals(request.OpaqueEvidence, ApprovedEvidence, StringComparison.Ordinal);
        return ValueTask.FromResult(new AgentToolApprovalResult
        {
            Decision = approved ? AgentToolApprovalDecision.Approved : AgentToolApprovalDecision.Denied,
            ClaimState = approved
                ? AgentToolApprovalEvidenceClaimState.Claimed
                : AgentToolApprovalEvidenceClaimState.Rejected,
            EvidenceId = approved ? "sample-evidence" : null,
            ApproverReference = approved ? "sample-approval-gate" : null,
            ReasonCode = approved ? "sample_approval_verified" : "approval_evidence_required"
        });
    }
}

public sealed class SampleAgentToolBudgetGate : IAgentToolBudgetGate
{
    private readonly ConcurrentDictionary<string, AgentToolBudgetReservation> _reservations = new(StringComparer.Ordinal);

    public bool DenyReservations { get; set; }

    public ValueTask<AgentToolBudgetReserveResult> ReserveAsync(
        AgentToolBudgetReserveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DenyReservations)
        {
            return ValueTask.FromResult(new AgentToolBudgetReserveResult
            {
                Status = AgentToolBudgetReserveStatus.Denied,
                ReasonCode = "sample_budget_denied"
            });
        }

        var governance = request.Context.Governance.Budget
            ?? throw new InvalidOperationException("A budget requirement is required.");
        var reservation = new AgentToolBudgetReservation
        {
            ReservationId = $"budget-{Guid.NewGuid():N}",
            AttemptId = request.Context.AttemptId,
            InvocationFingerprint = request.Context.InvocationFingerprint,
            Category = governance.Category,
            CostUnits = governance.CostUnits,
            MaxCallsPerExecution = governance.MaxCallsPerExecution,
            State = AgentToolBudgetReservationState.Reserved
        };
        _reservations[reservation.ReservationId] = reservation;
        return ValueTask.FromResult(new AgentToolBudgetReserveResult
        {
            Status = AgentToolBudgetReserveStatus.Reserved,
            Reservation = reservation
        });
    }

    public ValueTask<AgentToolBudgetReservation> FinalizeAsync(
        AgentToolBudgetFinalizeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_reservations.TryGetValue(request.ReservationId, out var existing))
            throw new InvalidOperationException("Budget reservation was not found.");
        if (existing.AttemptId != request.AttemptId
            || existing.InvocationFingerprint != request.InvocationFingerprint)
            throw new InvalidOperationException("Budget reservation binding mismatch.");

        var finalized = existing with { State = request.RequestedState };
        _reservations[existing.ReservationId] = finalized;
        return ValueTask.FromResult(finalized);
    }
}
