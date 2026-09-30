using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record PackageEvidencePreview
{
    public string PackagePreviewId { get; init; } = string.Empty;
    public string EvidencePreviewId { get; init; } = string.Empty;
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DraftAbstractions.DescriptorPackagePreview PackagePreview { get; init; }
    public required DescriptorPackageEvidence Evidence { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
}
