namespace CrestCreates.Agent.Memory.Tools;

// These types are physically owned by Projection.Abstractions
// but namespace remains CrestCreates.Agent.Memory.Tools for TypeForward compatibility.

public enum AgentMemoryResourceKind
{
    Unknown = 0,
    Context = 1,
    Candidate = 2,
    Memory = 3,
    ConversationHistory = 4,
    TaskHistory = 5
}

public enum AgentMemorySecurityArtifactState
{
    Unknown = 0,
    Active = 1,
    Revoked = 2,
    Expired = 3
}

public enum AgentMemorySecurityArtifactKind
{
    Unknown = 0,
    ResourceHandle = 1,
    SourceGrant = 2
}

public enum PreparedArtifactDisposition
{
    Unknown = 0,
    CreatedByBatch = 1,
    ReusedExisting = 2
}
