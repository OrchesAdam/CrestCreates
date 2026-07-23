using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Projection.Abstractions.Security;

public readonly record struct AgentMemorySourceKey(
    string TenantId,
    AgentSourceKind SourceKind,
    string SourceId,
    int? RangeStart,
    int? RangeEnd);
