namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Fail-closed: Unknown = 0 is rejected by Coordinator/Resolver at entry.
/// TrustedHostOperation = 2 preserves existing BatchOrigin ordinal.
/// </summary>
public enum AgentMemoryArtifactOriginKind
{
    Unknown = 0,
    AgentToolInvocation = 1,
    TrustedHostOperation = 2,
    McpInvocation = 3,
    McpSessionOperation = 4
}
