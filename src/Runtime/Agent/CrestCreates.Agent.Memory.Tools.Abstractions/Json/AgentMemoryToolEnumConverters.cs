using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Memory.Tools;

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

internal static class AgentMemoryToolEnumWire
{
    public static bool Match(string value, string expected) => string.Equals(value, expected, StringComparison.Ordinal);
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
