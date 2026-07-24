using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Memory.Tools;

// Enums and readonly DTOs shared between tools have been migrated to
// CrestCreates.Agent.Memory.Projection.Abstractions (TypeForward compatible).
// Write-only DTOs and CandidateStatus remain here.

[JsonConverter(typeof(AgentMemoryToolCandidateStatusJsonConverter))]
public enum AgentMemoryToolCandidateStatus
{
    Unknown = 0,
    Candidate = 1,
    Active = 2,
    Rejected = 3
}

// AgentMemoryToolCandidateDto stays in Tools.Abstractions because it depends on
// AgentMemoryToolCandidateStatus (also staying here). Moving it would cause a
// circular Projection.Abstractions ↔ Tools.Abstractions dependency.

public sealed record AgentMemoryToolCandidateDto
{
    public required string CandidateHandle { get; init; }
    public required AgentMemoryToolKind Kind { get; init; }
    public required string Content { get; init; }
    public required AgentMemoryToolCanonicalHashDto CanonicalContentHash { get; init; }
    public required AgentMemoryToolConfidence Confidence { get; init; }
    public required AgentMemoryToolCandidateStatus CandidateStatus { get; init; }
    public bool IsAuthoritative { get; init; }
    public IReadOnlyList<AgentMemorySourceGrantDto> SourceGrants { get; init; } = Array.Empty<AgentMemorySourceGrantDto>();
}

public sealed record CompressAgentHistoryInput
{
    public string HistorySourceHandle { get; init; } = string.Empty;
}

public sealed record ExtractMemoryCandidatesInput
{
    public string ContextHandle { get; init; } = string.Empty;
}

public sealed record PromoteMemoryCandidateInput
{
    public string CandidateHandle { get; init; } = string.Empty;
    public string? Explanation { get; init; }
}

public sealed record RejectMemoryCandidateInput
{
    public string CandidateHandle { get; init; } = string.Empty;
    public string? Explanation { get; init; }
}

public sealed record SupersedeMemoryItemInput
{
    public string MemoryHandle { get; init; } = string.Empty;
    public string ReplacementCandidateHandle { get; init; } = string.Empty;
    public string? Explanation { get; init; }
}

public sealed record CompressAgentHistoryResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public string? ContextHandle { get; init; }
    public AgentMemoryToolSourceKind? SourceKind { get; init; }
    public IReadOnlyList<AgentMemoryToolBlockDto> Blocks { get; init; } = Array.Empty<AgentMemoryToolBlockDto>();
    public int BlockCount { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}

public sealed record ExtractMemoryCandidatesResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public string? ContextHandle { get; init; }
    public IReadOnlyList<AgentMemoryToolCandidateDto> Candidates { get; init; } = Array.Empty<AgentMemoryToolCandidateDto>();
    public int CandidateCount { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}

public sealed record PromoteMemoryCandidateResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public AgentMemoryToolItemDto? Item { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}

public sealed record RejectMemoryCandidateResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public string? CandidateHandle { get; init; }
    public AgentMemoryToolCandidateStatus? CandidateStatus { get; init; }
    public bool IsAuthoritative { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}

public sealed record SupersedeMemoryItemResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public AgentMemoryToolItemDto? Item { get; init; }
    public string? SupersededMemoryHandle { get; init; }
    public string? ActiveMemoryHandle { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
