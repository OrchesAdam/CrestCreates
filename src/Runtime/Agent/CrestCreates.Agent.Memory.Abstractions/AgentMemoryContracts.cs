namespace CrestCreates.Agent.Memory.Abstractions;

public enum AgentSourceKind
{
    ConversationTurn = 0,
    TaskRecord = 1,
    TaskEvent = 2,
    CompressedContextBlock = 3,
    MemoryCandidate = 4,
    MemoryItem = 5,
    MetadataContextPack = 6,
    ReviewReport = 7,
    FixProposal = 8,
    PackagePreview = 9,
    ActivationRequest = 10
}

public enum AgentMemoryConfidence
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum AgentMemoryStatus
{
    Candidate = 0,
    Active = 1,
    Rejected = 2,
    Superseded = 3,
    Archived = 4
}

public enum AgentMemoryKind
{
    Preference = 0,
    ProjectFact = 1,
    Decision = 2,
    Constraint = 3,
    WorkflowHint = 4,
    Risk = 5
}

public enum AgentConversationRole
{
    User = 0,
    Assistant = 1,
    Tool = 2,
    System = 3
}

public enum AgentMemorySourceExpansionStatus
{
    Expanded = 0,
    NotExpandable = 1,
    ExternalSourceNotSupported = 2,
    NotFound = 3,
    Redacted = 4
}

public enum AgentMemoryOperationKind
{
    Promote = 0,
    Reject = 1,
    Supersede = 2,
    Archive = 3
}

public sealed record AgentContextSourceRef
{
    public required AgentSourceKind SourceKind { get; init; }
    public required string TenantId { get; init; }
    public required string SourceId { get; init; }
    public int? RangeStart { get; init; }
    public int? RangeEnd { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public CanonicalHash? CanonicalContentHash { get; init; }
}

public sealed record AgentContextEvidenceRef
{
    public required string EvidenceId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public CanonicalHash? CanonicalContentHash { get; init; }
}

public sealed record AgentMemoryDiagnostic
{
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }

    public SeverityLevel Severity { get; init; } = SeverityLevel.Info;
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
}

public sealed record AgentMemoryInvocationContext
{
    public required string TenantId { get; init; }
    public required string ActorId { get; init; }
    public required string ActorKind { get; init; }
    public string? AgentId { get; init; }
    public string? SessionId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? InvocationSource { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyDictionary<string, string> TraceAttributes { get; init; } = new Dictionary<string, string>();
}

public sealed record AgentConversationTurn
{
    public required string TurnId { get; init; }
    public required string TenantId { get; init; }
    public required AgentConversationRole Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentConversationRecord
{
    public required string ConversationId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentConversationTurn> Turns { get; init; } = Array.Empty<AgentConversationTurn>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentTaskEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string TaskId { get; init; }
    public required string EventKind { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentTaskRecord
{
    public required string TaskId { get; init; }
    public required string TenantId { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<AgentTaskEvent> Events { get; init; } = Array.Empty<AgentTaskEvent>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record SanitizedAgentContent
{
    public required string SanitizedContent { get; init; }
    public required CanonicalHash CanonicalContentHash { get; init; }
    public bool Rejected { get; init; }
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentCompressedContextBlock
{
    public required string BlockId { get; init; }
    public required string TenantId { get; init; }
    public required string Content { get; init; }
    public required CanonicalHash CanonicalContentHash { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public int ApproximateCharacterCount => Content.Length;
}

public sealed record AgentCompressedContext
{
    public required string ContextId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentCompressedContextBlock> Blocks { get; init; } = Array.Empty<AgentCompressedContextBlock>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentMemoryCandidate
{
    public required string CandidateId { get; init; }
    public required string TenantId { get; init; }
    public required AgentMemoryKind Kind { get; init; }
    public required string Content { get; init; }
    public required CanonicalHash CanonicalContentHash { get; init; }
    public AgentMemoryConfidence Confidence { get; init; } = AgentMemoryConfidence.Unknown;
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public AgentMemoryStatus Status { get; init; } = AgentMemoryStatus.Candidate;
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryDiagnostic> SanitizationDiagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentMemoryItem
{
    public required string MemoryId { get; init; }
    public required string TenantId { get; init; }
    public required AgentMemoryKind Kind { get; init; }
    public required string Content { get; init; }
    public required CanonicalHash CanonicalContentHash { get; init; }
    public required DateTimeOffset PromotedAt { get; init; }
    public AgentMemoryConfidence Confidence { get; init; } = AgentMemoryConfidence.Unknown;
    public AgentMemoryStatus Status { get; init; } = AgentMemoryStatus.Active;
    public bool IsAuthoritative { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public string? SupersedesMemoryId { get; init; }
    public string? SupersededByMemoryId { get; init; }
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryDiagnostic> SanitizationDiagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentMemoryQuery
{
    public required string TenantId { get; init; }
    public string? IntentText { get; init; }
    public IReadOnlyList<string> MemoryIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryKind> Kinds { get; init; } = Array.Empty<AgentMemoryKind>();
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorKind> VisibleDescriptorKinds { get; init; } = Array.Empty<DescriptorKind>();
    public int? MaxCount { get; init; }
    public int? CharacterBudget { get; init; }
    public AgentMemoryConfidence MinimumConfidence { get; init; } = AgentMemoryConfidence.Unknown;
    public bool IncludeStale { get; init; }
    public bool IncludeSuperseded { get; init; }
    public bool IncludeArchived { get; init; }
    public bool IncludeSourceRefs { get; init; } = true;
}

public sealed record AgentMemoryPack
{
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentMemoryItem> Memories { get; init; } = Array.Empty<AgentMemoryItem>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public bool IsAuthoritative { get; init; }
    public CanonicalHash? ScopeFingerprint { get; init; }
    public CanonicalHash? VisibleMemorySetHash { get; init; }
    public CanonicalHash? CanonicalPackHash { get; init; }
}

public sealed record AgentMemoryOperationRequest
{
    public required string TenantId { get; init; }
    public required AgentMemoryInvocationContext InvocationContext { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public string? Explanation { get; init; }
}

public sealed record AgentSourceExpansionResult
{
    public required AgentContextSourceRef SourceRef { get; init; }
    public required AgentMemorySourceExpansionStatus Status { get; init; }
    public string? SanitizedContent { get; init; }
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}

public sealed record AgentAuthoringRequest
{
    public required string TenantId { get; init; }
    public required string IntentText { get; init; }
    public AgentMemoryQuery? MemoryQuery { get; init; }
}

public sealed record AgentAuthoringContext
{
    public required AgentAuthoringRequest Request { get; init; }
    public required MetadataContextPack MetadataContextPack { get; init; }
    public required AgentMemoryPack MemoryPack { get; init; }
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
}
