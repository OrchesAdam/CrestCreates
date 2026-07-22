using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

public sealed record AgentMemoryAccessResolvedResource
{
    public required AgentMemoryAccessResourceHandle Handle { get; init; }
    public object? Resource { get; init; }
    public IReadOnlyList<DescriptorRef> EffectiveDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
}
