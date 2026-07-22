using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Memory.Tools;

// AgentMemoryToolEnumConverter<T> and all other enum converters have been migrated to
// CrestCreates.Agent.Memory.Projection.Abstractions (TypeForward compatible).
// Only AgentMemoryToolCandidateStatusJsonConverter remains here because
// AgentMemoryToolCandidateStatus stays in this assembly.

public sealed class AgentMemoryToolCandidateStatusJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolCandidateStatus>
{
    protected override string? ToWire(AgentMemoryToolCandidateStatus value) => value switch
    {
        AgentMemoryToolCandidateStatus.Candidate => "candidate",
        AgentMemoryToolCandidateStatus.Active => "active",
        AgentMemoryToolCandidateStatus.Rejected => "rejected",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolCandidateStatus result)
    {
        result = value switch
        {
            "candidate" => AgentMemoryToolCandidateStatus.Candidate,
            "active" => AgentMemoryToolCandidateStatus.Active,
            "rejected" => AgentMemoryToolCandidateStatus.Rejected,
            _ => AgentMemoryToolCandidateStatus.Unknown
        };
        return result != AgentMemoryToolCandidateStatus.Unknown;
    }
}
