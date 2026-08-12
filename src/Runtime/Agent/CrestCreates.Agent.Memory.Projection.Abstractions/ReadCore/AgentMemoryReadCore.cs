using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Shared memory recall core. Protocol-neutral — used by both Agent Tool and MCP handlers.
/// </summary>
public interface IAgentMemoryReadCore
{
    ValueTask<AgentMemoryReadCoreOutcome<BuildAgentMemoryPackResult>> RecallAsync(
        AgentMemoryRecallOperationRequest request,
        CancellationToken cancellationToken = default);
}
