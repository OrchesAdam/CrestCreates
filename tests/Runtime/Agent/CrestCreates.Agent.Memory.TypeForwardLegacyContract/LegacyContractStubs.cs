using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Legacy contract replica — these type stubs match the PRE-migration
/// Tools.Abstractions assembly identity. The LegacyConsumer compiles against
/// this replica; at runtime, the real forwarding assembly resolves type references
/// via [assembly: TypeForwardedTo].
/// 
/// DO NOT add [assembly: TypeForwardedTo] here — that would make this
/// a forwarding assembly instead of a pre-migration contract.
/// </summary>

// ── Enums ──────────────────────────────────────────────────

[JsonConverter(typeof(AgentMemoryToolOperationStatusJsonConverter))]
public enum AgentMemoryToolOperationStatus
{
    Unknown = 0, Completed = 1, Unavailable = 2, Conflict = 3, Redacted = 4, NotExpandable = 5
}

[JsonConverter(typeof(AgentMemoryToolMemoryStatusJsonConverter))]
public enum AgentMemoryToolMemoryStatus
{
    Unknown = 0, Active = 1, Superseded = 2, Archived = 3
}

[JsonConverter(typeof(AgentMemoryToolKindJsonConverter))]
public enum AgentMemoryToolKind
{
    Unknown = 0, Preference = 1, ProjectFact = 2, Decision = 3,
    Constraint = 4, WorkflowHint = 5, Risk = 6
}

[JsonConverter(typeof(AgentMemoryToolConfidenceJsonConverter))]
public enum AgentMemoryToolConfidence
{
    Unknown = 0, Unspecified = 1, Low = 2, Medium = 3, High = 4
}

[JsonConverter(typeof(AgentMemoryToolSourceKindJsonConverter))]
public enum AgentMemoryToolSourceKind
{
    Unknown = 0, ConversationTurn = 1, TaskRecord = 2, TaskEvent = 3,
    CompressedContextBlock = 4, MemoryCandidate = 5, MemoryItem = 6,
    MetadataContextPack = 7, ReviewReport = 8, FixProposal = 9,
    PackagePreview = 10, ActivationRequest = 11
}

[JsonConverter(typeof(AgentMemoryToolDiagnosticSeverityJsonConverter))]
public enum AgentMemoryToolDiagnosticSeverity
{
    Unknown = 0, Info = 1, Warning = 2, Error = 3
}

public enum AgentMemoryResourceKind
{
    Unknown = 0, Context = 1, Candidate = 2, Memory = 3,
    ConversationHistory = 4, TaskHistory = 5, TaskEvent = 6
}

public enum AgentMemorySecurityArtifactState
{
    Unknown = 0, Active = 1, Revoked = 2, Expired = 3
}

public enum AgentMemorySecurityArtifactKind
{
    Unknown = 0, ResourceHandle = 1, SourceGrant = 2
}

public enum PreparedArtifactDisposition
{
    Unknown = 0, CreatedByBatch = 1, ReusedExisting = 2
}

// ── DTOs ───────────────────────────────────────────────────

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

// ── JSON Converters ────────────────────────────────────────

public abstract class AgentMemoryToolEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    protected abstract string? ToWire(T value);
    protected abstract bool TryParse(string value, out T result);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !TryParse(reader.GetString() ?? string.Empty, out var result))
            throw new JsonException($"Invalid {typeof(T).Name} wire value.");
        return result;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var wire = ToWire(value) ?? throw new JsonException($"Unknown {typeof(T).Name} value.");
        writer.WriteStringValue(wire);
    }
}

public sealed class AgentMemoryToolOperationStatusJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolOperationStatus>
{
    protected override string? ToWire(AgentMemoryToolOperationStatus value) => value switch
    {
        AgentMemoryToolOperationStatus.Completed => "completed",
        AgentMemoryToolOperationStatus.Unavailable => "unavailable",
        AgentMemoryToolOperationStatus.Conflict => "conflict",
        AgentMemoryToolOperationStatus.Redacted => "redacted",
        AgentMemoryToolOperationStatus.NotExpandable => "not-expandable",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolOperationStatus result)
    {
        result = value switch
        {
            "completed" => AgentMemoryToolOperationStatus.Completed,
            "unavailable" => AgentMemoryToolOperationStatus.Unavailable,
            "conflict" => AgentMemoryToolOperationStatus.Conflict,
            "redacted" => AgentMemoryToolOperationStatus.Redacted,
            "not-expandable" => AgentMemoryToolOperationStatus.NotExpandable,
            _ => AgentMemoryToolOperationStatus.Unknown
        };
        return result != AgentMemoryToolOperationStatus.Unknown;
    }
}

public sealed class AgentMemoryToolMemoryStatusJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolMemoryStatus>
{
    protected override string? ToWire(AgentMemoryToolMemoryStatus value) => value switch
    {
        AgentMemoryToolMemoryStatus.Active => "active",
        AgentMemoryToolMemoryStatus.Superseded => "superseded",
        AgentMemoryToolMemoryStatus.Archived => "archived",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolMemoryStatus result)
    {
        result = value switch
        {
            "active" => AgentMemoryToolMemoryStatus.Active,
            "superseded" => AgentMemoryToolMemoryStatus.Superseded,
            "archived" => AgentMemoryToolMemoryStatus.Archived,
            _ => AgentMemoryToolMemoryStatus.Unknown
        };
        return result != AgentMemoryToolMemoryStatus.Unknown;
    }
}

public sealed class AgentMemoryToolKindJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolKind>
{
    protected override string? ToWire(AgentMemoryToolKind value) => value switch
    {
        AgentMemoryToolKind.Preference => "preference",
        AgentMemoryToolKind.ProjectFact => "project-fact",
        AgentMemoryToolKind.Decision => "decision",
        AgentMemoryToolKind.Constraint => "constraint",
        AgentMemoryToolKind.WorkflowHint => "workflow-hint",
        AgentMemoryToolKind.Risk => "risk",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolKind result)
    {
        result = value switch
        {
            "preference" => AgentMemoryToolKind.Preference,
            "project-fact" => AgentMemoryToolKind.ProjectFact,
            "decision" => AgentMemoryToolKind.Decision,
            "constraint" => AgentMemoryToolKind.Constraint,
            "workflow-hint" => AgentMemoryToolKind.WorkflowHint,
            "risk" => AgentMemoryToolKind.Risk,
            _ => AgentMemoryToolKind.Unknown
        };
        return result != AgentMemoryToolKind.Unknown;
    }
}

public sealed class AgentMemoryToolConfidenceJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolConfidence>
{
    protected override string? ToWire(AgentMemoryToolConfidence value) => value switch
    {
        AgentMemoryToolConfidence.Unspecified => "unknown",
        AgentMemoryToolConfidence.Low => "low",
        AgentMemoryToolConfidence.Medium => "medium",
        AgentMemoryToolConfidence.High => "high",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolConfidence result)
    {
        result = value switch
        {
            "unknown" => AgentMemoryToolConfidence.Unspecified,
            "low" => AgentMemoryToolConfidence.Low,
            "medium" => AgentMemoryToolConfidence.Medium,
            "high" => AgentMemoryToolConfidence.High,
            _ => AgentMemoryToolConfidence.Unknown
        };
        return result != AgentMemoryToolConfidence.Unknown;
    }
}

public sealed class AgentMemoryToolSourceKindJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolSourceKind>
{
    protected override string? ToWire(AgentMemoryToolSourceKind value) => value switch
    {
        AgentMemoryToolSourceKind.ConversationTurn => "conversation-turn",
        AgentMemoryToolSourceKind.TaskRecord => "task-record",
        AgentMemoryToolSourceKind.TaskEvent => "task-event",
        AgentMemoryToolSourceKind.CompressedContextBlock => "compressed-context-block",
        AgentMemoryToolSourceKind.MemoryCandidate => "memory-candidate",
        AgentMemoryToolSourceKind.MemoryItem => "memory-item",
        AgentMemoryToolSourceKind.MetadataContextPack => "metadata-context-pack",
        AgentMemoryToolSourceKind.ReviewReport => "review-report",
        AgentMemoryToolSourceKind.FixProposal => "fix-proposal",
        AgentMemoryToolSourceKind.PackagePreview => "package-preview",
        AgentMemoryToolSourceKind.ActivationRequest => "activation-request",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolSourceKind result)
    {
        result = value switch
        {
            "conversation-turn" => AgentMemoryToolSourceKind.ConversationTurn,
            "task-record" => AgentMemoryToolSourceKind.TaskRecord,
            "task-event" => AgentMemoryToolSourceKind.TaskEvent,
            "compressed-context-block" => AgentMemoryToolSourceKind.CompressedContextBlock,
            "memory-candidate" => AgentMemoryToolSourceKind.MemoryCandidate,
            "memory-item" => AgentMemoryToolSourceKind.MemoryItem,
            "metadata-context-pack" => AgentMemoryToolSourceKind.MetadataContextPack,
            "review-report" => AgentMemoryToolSourceKind.ReviewReport,
            "fix-proposal" => AgentMemoryToolSourceKind.FixProposal,
            "package-preview" => AgentMemoryToolSourceKind.PackagePreview,
            "activation-request" => AgentMemoryToolSourceKind.ActivationRequest,
            _ => AgentMemoryToolSourceKind.Unknown
        };
        return result != AgentMemoryToolSourceKind.Unknown;
    }
}

public sealed class AgentMemoryToolDiagnosticSeverityJsonConverter : AgentMemoryToolEnumConverter<AgentMemoryToolDiagnosticSeverity>
{
    protected override string? ToWire(AgentMemoryToolDiagnosticSeverity value) => value switch
    {
        AgentMemoryToolDiagnosticSeverity.Info => "info",
        AgentMemoryToolDiagnosticSeverity.Warning => "warning",
        AgentMemoryToolDiagnosticSeverity.Error => "error",
        _ => null
    };
    protected override bool TryParse(string value, out AgentMemoryToolDiagnosticSeverity result)
    {
        result = value switch
        {
            "info" => AgentMemoryToolDiagnosticSeverity.Info,
            "warning" => AgentMemoryToolDiagnosticSeverity.Warning,
            "error" => AgentMemoryToolDiagnosticSeverity.Error,
            _ => AgentMemoryToolDiagnosticSeverity.Unknown
        };
        return result != AgentMemoryToolDiagnosticSeverity.Unknown;
    }
}
