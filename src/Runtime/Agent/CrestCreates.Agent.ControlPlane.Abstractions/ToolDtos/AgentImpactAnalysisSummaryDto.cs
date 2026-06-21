using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of impact analysis.
/// Replaces DescriptorImpactAnalysisReport internals.
/// </summary>
public sealed record AgentImpactAnalysisSummaryDto
{
    public required IReadOnlyList<DescriptorRef> AffectedDescriptors { get; init; }
    public required int TotalAffectedCount { get; init; }
    public required string Severity { get; init; }
    public string? Summary { get; init; }
}
