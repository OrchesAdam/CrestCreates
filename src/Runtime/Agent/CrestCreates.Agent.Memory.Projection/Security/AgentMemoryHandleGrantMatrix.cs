using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Explicit Handle/Grant support matrix.
/// Centralizes which ResourceKinds can be issued as Handles vs Grants.
/// Coordinator, Resolver, and Issuer must all consult this matrix.
/// </summary>
internal static class AgentMemoryHandleGrantMatrix
{
    /// <summary>
    /// Whether the given ResourceKind supports Handle issuance.
    /// </summary>
    public static bool IsHandleSupported(AgentMemoryResourceKind kind) => kind switch
    {
        AgentMemoryResourceKind.ConversationHistory => true,
        AgentMemoryResourceKind.TaskHistory => true,
        AgentMemoryResourceKind.Context => true,
        AgentMemoryResourceKind.Memory => true,
        AgentMemoryResourceKind.Candidate => true,
        // TaskEvent is Grant-only — cannot issue a Handle for it
        _ => false
    };

    /// <summary>
    /// Whether the given ResourceKind supports Grant issuance.
    /// </summary>
    public static bool IsGrantSupported(AgentMemoryResourceKind kind) => kind switch
    {
        AgentMemoryResourceKind.ConversationHistory => true,
        AgentMemoryResourceKind.TaskHistory => true,
        AgentMemoryResourceKind.TaskEvent => true,
        AgentMemoryResourceKind.Context => true,
        AgentMemoryResourceKind.Memory => true,
        AgentMemoryResourceKind.Candidate => true,
        _ => false
    };

    /// <summary>
    /// Whether the given ResourceKind is a "history" resource that uses
    /// existence-only validation (no descriptor closure comparison).
    /// Only ConversationHistory and TaskHistory — NOT TaskEvent.
    /// </summary>
    public static bool IsHistoryHandleKind(AgentMemoryResourceKind kind) => kind switch
    {
        AgentMemoryResourceKind.ConversationHistory => true,
        AgentMemoryResourceKind.TaskHistory => true,
        _ => false
    };
}
