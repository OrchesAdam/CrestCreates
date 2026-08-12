using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.ReadCore.Accountability;

/// <summary>
/// Explicit semantic mappings for values that cross the Memory Accountability
/// boundary. These are deliberately not derived from CLR enum names.
/// </summary>
internal static class AgentMemoryStableWireMappings
{
    public static string MapRequestedKind(AgentMemoryToolKind kind) => kind switch
    {
        AgentMemoryToolKind.Preference => "Preference",
        AgentMemoryToolKind.ProjectFact => "ProjectFact",
        AgentMemoryToolKind.Decision => "Decision",
        AgentMemoryToolKind.Constraint => "Constraint",
        AgentMemoryToolKind.WorkflowHint => "WorkflowHint",
        AgentMemoryToolKind.Risk => "Risk",
        _ => throw new InvalidOperationException("Unknown Memory tool kind cannot enter Accountability.")
    };

    public static string MapSourceKind(AgentSourceKind kind) => kind switch
    {
        AgentSourceKind.ConversationTurn => "ConversationTurn",
        AgentSourceKind.TaskRecord => "TaskRecord",
        AgentSourceKind.TaskEvent => "TaskEvent",
        AgentSourceKind.CompressedContextBlock => "CompressedContextBlock",
        AgentSourceKind.MemoryCandidate => "MemoryCandidate",
        AgentSourceKind.MemoryItem => "MemoryItem",
        _ => throw new InvalidOperationException("Unsupported SourceKind cannot enter Accountability.")
    };
}
