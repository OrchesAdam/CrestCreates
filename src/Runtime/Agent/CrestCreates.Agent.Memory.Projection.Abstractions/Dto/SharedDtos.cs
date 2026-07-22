using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Memory.Tools;

// These types are physically owned by Projection.Abstractions
// but namespace remains CrestCreates.Agent.Memory.Tools for TypeForward compatibility.

[JsonConverter(typeof(AgentMemoryToolOperationStatusJsonConverter))]
public enum AgentMemoryToolOperationStatus
{
    Unknown = 0,
    Completed = 1,
    Unavailable = 2,
    Conflict = 3,
    Redacted = 4,
    NotExpandable = 5
}

[JsonConverter(typeof(AgentMemoryToolMemoryStatusJsonConverter))]
public enum AgentMemoryToolMemoryStatus
{
    Unknown = 0,
    Active = 1,
    Superseded = 2,
    Archived = 3
}

[JsonConverter(typeof(AgentMemoryToolKindJsonConverter))]
public enum AgentMemoryToolKind
{
    Unknown = 0,
    Preference = 1,
    ProjectFact = 2,
    Decision = 3,
    Constraint = 4,
    WorkflowHint = 5,
    Risk = 6
}

[JsonConverter(typeof(AgentMemoryToolConfidenceJsonConverter))]
public enum AgentMemoryToolConfidence
{
    Unknown = 0,
    Unspecified = 1,
    Low = 2,
    Medium = 3,
    High = 4
}

[JsonConverter(typeof(AgentMemoryToolSourceKindJsonConverter))]
public enum AgentMemoryToolSourceKind
{
    Unknown = 0,
    ConversationTurn = 1,
    TaskRecord = 2,
    TaskEvent = 3,
    CompressedContextBlock = 4,
    MemoryCandidate = 5,
    MemoryItem = 6,
    MetadataContextPack = 7,
    ReviewReport = 8,
    FixProposal = 9,
    PackagePreview = 10,
    ActivationRequest = 11
}

[JsonConverter(typeof(AgentMemoryToolDiagnosticSeverityJsonConverter))]
public enum AgentMemoryToolDiagnosticSeverity
{
    Unknown = 0,
    Info = 1,
    Warning = 2,
    Error = 3
}

// Read-only DTOs (TypeForward-safe — no security types in members)

public sealed record AgentMemoryToolCanonicalHashDto
{
    public required string Value { get; init; }
    public required string AlgorithmVersion { get; init; }
    public required string ContractVersion { get; init; }
    public required string CanonicalShapeVersion { get; init; }
}

public sealed record AgentMemorySourceGrantDto
{
    public required string GrantId { get; init; }
    public required AgentMemoryToolSourceKind SourceKind { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record AgentMemoryToolDiagnosticDto
{
    public required string Code { get; init; }
    public required AgentMemoryToolDiagnosticSeverity Severity { get; init; }
}

public sealed record AgentMemoryToolItemDto
{
    public required string MemoryHandle { get; init; }
    public required AgentMemoryToolKind Kind { get; init; }
    public required string Content { get; init; }
    public required AgentMemoryToolCanonicalHashDto CanonicalContentHash { get; init; }
    public required AgentMemoryToolConfidence Confidence { get; init; }
    public required AgentMemoryToolMemoryStatus MemoryStatus { get; init; }
    public bool IsAuthoritative { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemorySourceGrantDto> SourceGrants { get; init; } = Array.Empty<AgentMemorySourceGrantDto>();
}

// AgentMemoryToolCandidateDto stays in Tools.Abstractions —
// it depends on AgentMemoryToolCandidateStatus which also stays in Tools.Abstractions.
// Moving it would create a circular Projection.Abstractions ↔ Tools.Abstractions dependency.

public sealed record AgentMemoryToolBlockDto
{
    public required string Content { get; init; }
    public required AgentMemoryToolCanonicalHashDto CanonicalContentHash { get; init; }
    public IReadOnlyList<AgentMemorySourceGrantDto> SourceGrants { get; init; } = Array.Empty<AgentMemorySourceGrantDto>();
}

public sealed record BuildAgentMemoryPackInput
{
    public IReadOnlyList<string> MemoryHandles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryToolKind> Kinds { get; init; } = Array.Empty<AgentMemoryToolKind>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public int MaximumCount { get; init; }
    public int CharacterBudget { get; init; }
    public AgentMemoryToolConfidence MinimumConfidence { get; init; } = AgentMemoryToolConfidence.Unspecified;
}

public sealed record ExpandAgentMemorySourceInput
{
    public string GrantId { get; init; } = string.Empty;
    public int MaximumCharacters { get; init; }
}

public sealed record BuildAgentMemoryPackResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public IReadOnlyList<AgentMemoryToolItemDto> Items { get; init; } = Array.Empty<AgentMemoryToolItemDto>();
    public int ReturnedCount { get; init; }
    public bool WasTruncated { get; init; }
    public bool IsAuthoritative { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}

public sealed record ExpandAgentMemorySourceResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public string? SanitizedContent { get; init; }
    public AgentMemoryToolCanonicalHashDto? CanonicalContentHash { get; init; }
    public bool WasTruncated { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
