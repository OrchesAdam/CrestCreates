using CrestCreates.Agent.Abstractions;
using CrestCreates.Agent.Tools;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetAgentToolApprovalGate : IAgentToolApprovalGate
{
    public ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(AgentToolApprovalRequest request, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new AgentToolApprovalResult { Decision = AgentToolApprovalDecision.NotRequired, ClaimState = AgentToolApprovalEvidenceClaimState.NotApplicable, ReasonCode = "read_only" });
}
