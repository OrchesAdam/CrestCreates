namespace CrestCreates.Agent.Tools;

public enum AgentToolApprovalDecision
{
    Unknown = 0,
    Denied = 1,
    NotRequired = 2,
    Approved = 3
}

public enum AgentToolApprovalEvidenceClaimState
{
    Unknown = 0,
    NotApplicable = 1,
    Claimed = 2,
    Rejected = 3
}

public sealed record AgentToolApprovalRequest
{
    public required AgentToolGovernanceContext Context { get; init; }

    public string? OpaqueEvidence { get; init; }
}

public sealed record AgentToolApprovalResult
{
    public required AgentToolApprovalDecision Decision { get; init; }

    public required AgentToolApprovalEvidenceClaimState ClaimState { get; init; }

    public string? EvidenceId { get; init; }

    public string? ApproverReference { get; init; }

    public string? ReasonCode { get; init; }
}

public interface IAgentToolApprovalGate
{
    ValueTask<AgentToolApprovalResult> EvaluateAndClaimAsync(
        AgentToolApprovalRequest request,
        CancellationToken cancellationToken = default);
}
