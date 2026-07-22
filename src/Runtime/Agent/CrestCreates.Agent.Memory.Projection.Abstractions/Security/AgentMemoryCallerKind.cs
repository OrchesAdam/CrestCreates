namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Fail-closed: Unknown = 0 is rejected by Coordinator/Resolver at entry.
/// </summary>
public enum AgentMemoryCallerKind
{
    Unknown = 0,
    AgentTool = 1,
    Mcp = 3
}
