using CrestCreates.Agent.ControlPlane.Abstractions.Json;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposal
{
    public required string Id { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required FixProposalKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Explanation { get; init; }
    public required string ReasonCode { get; init; }

    public required FixProposalApplicability Applicability { get; init; }
    public required bool IsExecutable { get; init; }
    public required bool RequiresManualAction { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required bool BlocksActivationUntilResolved { get; init; }
    public required FixProposalRiskLevel RiskLevel { get; init; }

    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];
    public required IReadOnlyList<FixProposalAction> Actions { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Rationale { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;
}
