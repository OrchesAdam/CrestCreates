using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Provides the current descriptor closure for a resource at resolution time.
/// Returns null if the resource no longer exists.
/// </summary>
public interface IAgentMemoryCurrentClosureProvider
{
    ValueTask<AgentMemoryCurrentClosure?> GetCurrentClosureAsync(
        AgentMemoryResourceKind resourceKind,
        string tenantId,
        string resourceId,
        AgentContextSourceRef? sourceRef = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Current descriptor closure of a live resource. Null means resource not found.
/// </summary>
public sealed record AgentMemoryCurrentClosure
{
    public required IReadOnlyList<DescriptorRef> CurrentDescriptorRefs { get; init; }
    public required string TenantId { get; init; }
}
