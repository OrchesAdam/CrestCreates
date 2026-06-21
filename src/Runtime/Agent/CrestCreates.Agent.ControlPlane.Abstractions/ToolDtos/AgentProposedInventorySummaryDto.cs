using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of proposed inventory.
/// Replaces IReadOnlyList&lt;IDescriptor&gt; with descriptor refs only.
/// </summary>
public sealed record AgentProposedInventorySummaryDto
{
    public required IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; }
    public required int TotalCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> CountsByKind { get; init; }
}
