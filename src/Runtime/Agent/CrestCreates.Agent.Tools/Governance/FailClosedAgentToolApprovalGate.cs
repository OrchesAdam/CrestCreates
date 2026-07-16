using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;

namespace CrestCreates.Agent.Tools;

/// <summary>
/// Verifies and atomically claims approval evidence after the framework has
/// established the effective governance floor for an Agent Tool call.
/// </summary>
public interface IAgentToolApprovalEvidenceVerifier
{
    ValueTask<AgentToolApprovalResult> VerifyAndClaimAsync(
        AgentToolApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies the non-lowering approval floor and converts verifier failures or
/// malformed verifier results into an explicit denial.
/// </summary>
public sealed class FailClosedAgentToolApprovalGate : IAgentToolApprovalGate
{
    private readonly IAgentToolApprovalEvidenceVerifier? _verifier;

    public FailClosedAgentToolApprovalGate(
        IAgentToolApprovalEvidenceVerifier? verifier = null)
    {
        _verifier = verifier;
    }

    public async ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(
        AgentToolApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AgentToolGovernanceGuard.IsValid(request.Context))
        {
            return Denied("approval_context_invalid");
        }

        var governance = request.Context.Governance;
        var forcedApproval = governance.EffectiveRisk is CapabilityRiskLevel.High
                or CapabilityRiskLevel.Critical
            || governance.SideEffectKind is AgentToolSideEffectKind.ExternalWrite
                or AgentToolSideEffectKind.Destructive;

        var approvalRequired = forcedApproval
            || governance.EffectiveApprovalMode == AgentToolApprovalMode.Required;

        if (!approvalRequired
            && governance.EffectiveApprovalMode == AgentToolApprovalMode.None)
        {
            return new AgentToolApprovalResult
            {
                Decision = AgentToolApprovalDecision.NotRequired,
                ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable,
                ReasonCode = "approval_not_required"
            };
        }

        if (_verifier is null
            || approvalRequired && string.IsNullOrWhiteSpace(request.OpaqueEvidence))
        {
            return Denied("approval_evidence_required");
        }

        AgentToolApprovalResult result;
        try
        {
            result = await _verifier.VerifyAndClaimAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Denied("approval_verifier_failure");
        }

        if (result is
            {
                Decision: AgentToolApprovalDecision.Denied,
                ClaimState: AgentToolApprovalEvidenceClaimState.Rejected,
                ReasonCode: { Length: > 0 }
            })
        {
            return result;
        }

        var approvedClaim = result is not null
            && result.Decision == AgentToolApprovalDecision.Approved
            && result.ClaimState == AgentToolApprovalEvidenceClaimState.Claimed
            && !string.IsNullOrWhiteSpace(result.EvidenceId);
        var policyNotRequired = !approvalRequired
            && governance.EffectiveApprovalMode == AgentToolApprovalMode.PolicyDriven
            && result is not null
            && result.Decision == AgentToolApprovalDecision.NotRequired
            && result.ClaimState == AgentToolApprovalEvidenceClaimState.NotApplicable
            && string.IsNullOrWhiteSpace(result.EvidenceId);

        if (!approvedClaim && !policyNotRequired)
        {
            return Denied("approval_evidence_rejected");
        }

        return result!;
    }

    private static AgentToolApprovalResult Denied(string reasonCode)
        => new()
        {
            Decision = AgentToolApprovalDecision.Denied,
            ClaimState = AgentToolApprovalEvidenceClaimState.Rejected,
            ReasonCode = reasonCode
        };
}
