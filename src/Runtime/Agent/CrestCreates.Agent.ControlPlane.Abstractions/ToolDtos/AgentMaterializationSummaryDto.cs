using CrestCreates.Metadata.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of materialization result.
/// Replaces DescriptorDraftMaterializationResult which contains IReadOnlyList&lt;IDescriptor&gt;.
/// </summary>
public sealed record AgentMaterializationSummaryDto
{
    public required bool IsMaterialized { get; init; }
    public required IReadOnlyList<DescriptorRef> ProposedInventoryRefs { get; init; }
    public required IReadOnlyList<DraftAbstractions.DescriptorDraftDiagnostic> Diagnostics { get; init; }
}
