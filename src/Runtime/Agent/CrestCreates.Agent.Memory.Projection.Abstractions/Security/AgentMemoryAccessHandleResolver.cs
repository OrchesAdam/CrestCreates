using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Projection-neutral handle resolver. Full Principal record equality required.
/// </summary>
public interface IAgentMemoryAccessHandleResolver
{
    ValueTask<AgentMemoryAccessResolvedResource?> ResolveAsync(
        string handleId,
        AgentMemoryResourceKind expectedKind,
        AgentMemoryAccessPrincipal principal,
        AgentMemoryAccessScope scope,
        CancellationToken cancellationToken = default);
}
