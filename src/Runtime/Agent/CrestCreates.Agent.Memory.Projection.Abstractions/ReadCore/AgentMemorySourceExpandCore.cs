using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Shared source expand core. Protocol-neutral. Zero artifact writes —
/// ctx_expand and memory_source_expand must not issue any handles/grants.
/// </summary>
public interface IAgentMemorySourceExpandCore
{
    ValueTask<AgentMemoryReadCoreOutcome<ExpandAgentMemorySourceResult>> ExpandAsync(
        AgentMemorySourceExpansionOperationRequest request,
        CancellationToken cancellationToken = default);
}
