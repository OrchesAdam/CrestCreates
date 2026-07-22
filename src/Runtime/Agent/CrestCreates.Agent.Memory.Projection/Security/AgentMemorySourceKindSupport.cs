using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Security;

/// <summary>
/// Unified SourceKind support matrix for Issuer, Resolver, and Expander.
/// Unsupported SourceKinds must not issue grants.
/// </summary>
internal static class AgentMemorySourceKindSupport
{
    private static readonly HashSet<AgentSourceKind> SupportedGrantSourceKinds = new()
    {
        AgentSourceKind.ConversationTurn,
        AgentSourceKind.TaskRecord,
        AgentSourceKind.TaskEvent,
        AgentSourceKind.CompressedContextBlock,
        AgentSourceKind.MemoryCandidate,
        AgentSourceKind.MemoryItem,
    };

    public static bool IsGrantSupported(AgentSourceKind kind) => SupportedGrantSourceKinds.Contains(kind);

    public static AgentMemoryResourceKind ToResourceKind(AgentSourceKind kind) => kind switch
    {
        AgentSourceKind.ConversationTurn => AgentMemoryResourceKind.ConversationHistory,
        AgentSourceKind.TaskRecord => AgentMemoryResourceKind.TaskHistory,
        AgentSourceKind.TaskEvent => AgentMemoryResourceKind.TaskEvent,
        AgentSourceKind.CompressedContextBlock => AgentMemoryResourceKind.Context,
        AgentSourceKind.MemoryCandidate => AgentMemoryResourceKind.Candidate,
        AgentSourceKind.MemoryItem => AgentMemoryResourceKind.Memory,
        _ => throw new InvalidOperationException($"Unsupported grant SourceKind: {kind}")
    };
}
