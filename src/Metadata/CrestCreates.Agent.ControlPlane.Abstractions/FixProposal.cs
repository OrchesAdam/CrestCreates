namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposal
{
    public required string ProposalId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required FixProposalRiskLevel RiskLevel { get; init; }
    public required bool RequiresHumanApproval { get; init; }
    public required IReadOnlyList<FixProposalAction> Actions { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Rationale { get; init; }
}
