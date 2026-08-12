using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Snapshot.Abstractions;

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

public sealed record AgentContextSourceRef : ISnapshotable<AgentContextSourceRef>
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

    public AgentContextSourceRef Snapshot() => this with
    {
        DescriptorRefs = DescriptorRefs.ToArray()
    };
}

public sealed record AgentContextEvidenceRef : ISnapshotable<AgentContextEvidenceRef>
{
    public required string EvidenceId { get; init; }
    public required string EvidenceKind { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public CanonicalHash? CanonicalContentHash { get; init; }

    public AgentContextEvidenceRef Snapshot() => this with
    {
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentMemoryDiagnostic : ISnapshotable<AgentMemoryDiagnostic>
{
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }

    public SeverityLevel Severity { get; init; } = SeverityLevel.Info;
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();

    public AgentMemoryDiagnostic Snapshot() => this with
    {
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentMemoryInvocationContext : ISnapshotable<AgentMemoryInvocationContext>
{
    public required string TenantId { get; init; }
    public required string ActorId { get; init; }
    public required string ActorKind { get; init; }
    public string? AgentId { get; init; }
    public string? SessionId { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? ParentAuditId { get; init; }
    public string? InvocationSource { get; init; }
    public string? DisplayName { get; init; }
    public IReadOnlyDictionary<string, string> TraceAttributes { get; init; } = new Dictionary<string, string>();

    public AgentMemoryInvocationContext Snapshot() => this with
    {
        TraceAttributes = TraceAttributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
    };
}

public sealed record AgentConversationTurn : ISnapshotable<AgentConversationTurn>
{
    public required string TurnId { get; init; }
    public required string TenantId { get; init; }
    public required AgentConversationRole Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public AgentConversationTurn Snapshot() => this with
    {
        DescriptorRefs = DescriptorRefs.ToArray(),
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentConversationRecord : ISnapshotable<AgentConversationRecord>
{
    public required string ConversationId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentConversationTurn> Turns { get; init; } = Array.Empty<AgentConversationTurn>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public AgentConversationRecord Snapshot() => this with
    {
        Turns = Turns.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentTaskEvent : ISnapshotable<AgentTaskEvent>
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string TaskId { get; init; }
    public required string EventKind { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public AgentTaskEvent Snapshot() => this with
    {
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentTaskRecord : ISnapshotable<AgentTaskRecord>
{
    public required string TaskId { get; init; }
    public required string TenantId { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<AgentTaskEvent> Events { get; init; } = Array.Empty<AgentTaskEvent>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public AgentTaskRecord Snapshot() => this with
    {
        Events = Events.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record SanitizedAgentContent : ISnapshotable<SanitizedAgentContent>
{
    public required string SanitizedContent { get; init; }
    public required CanonicalHash CanonicalContentHash { get; init; }
    public bool Rejected { get; init; }
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public SanitizedAgentContent Snapshot() => this with
    {
        RedactionKinds = RedactionKinds.ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentCompressedContextBlock : ISnapshotable<AgentCompressedContextBlock>
{
    public required string BlockId { get; init; }
    public required string TenantId { get; init; }
    public required string Content { get; init; }
    public required CanonicalHash CanonicalContentHash { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public int ApproximateCharacterCount => Content.Length;

    public AgentCompressedContextBlock Snapshot() => this with
    {
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentCompressedContext : ISnapshotable<AgentCompressedContext>
{
    public required string ContextId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentCompressedContextBlock> Blocks { get; init; } = Array.Empty<AgentCompressedContextBlock>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public AgentPromptInputEvidenceSummary? PromptInputEvidence { get; init; }
    public AgentPromptOutputEvidenceSummary? PromptOutputEvidence { get; init; }
    public CanonicalHash? CanonicalOutputHash { get; init; }

    public AgentCompressedContext Snapshot() => this with
    {
        Blocks = Blocks.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentMemoryCandidate : ISnapshotable<AgentMemoryCandidate>
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
    public AgentPromptInputEvidenceSummary? PromptInputEvidence { get; init; }
    public AgentPromptOutputEvidenceSummary? PromptOutputEvidence { get; init; }
    public CanonicalHash? CanonicalOutputHash { get; init; }

    public AgentMemoryCandidate Snapshot() => this with
    {
        Tags = Tags.ToArray(),
        DescriptorRefs = DescriptorRefs.ToArray(),
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray(),
        RedactionKinds = RedactionKinds.ToArray(),
        SanitizationDiagnostics = SanitizationDiagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentMemoryItem : ISnapshotable<AgentMemoryItem>
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

    public AgentMemoryItem Snapshot() => this with
    {
        Tags = Tags.ToArray(),
        DescriptorRefs = DescriptorRefs.ToArray(),
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray(),
        RedactionKinds = RedactionKinds.ToArray(),
        SanitizationDiagnostics = SanitizationDiagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentMemoryQuery
{
    public required string TenantId { get; init; }
    /// <summary>
    /// The closed-world visibility boundary. Tool callers must provide this
    /// exact-version boundary; legacy fields remain only for existing runtime
    /// callers during migration and are ignored when this value is present.
    /// </summary>
    public AgentMemoryVisibilityBoundary? VisibilityBoundary { get; init; }
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

    /// <summary>
    /// Deep copy for boundary snapshot isolation. Not ISnapshotable because
    /// AgentMemoryQuery is a request/filter model, not a boundary state model.
    /// </summary>
    public AgentMemoryQuery Copy() => this with
    {
        VisibilityBoundary = VisibilityBoundary?.Snapshot(),
        MemoryIds = MemoryIds.ToArray(),
        Kinds = Kinds.ToArray(),
        Tags = Tags.ToArray(),
        DescriptorRefs = DescriptorRefs.ToArray(),
        VisibleDescriptorRefs = VisibleDescriptorRefs.ToArray(),
        VisibleDescriptorKinds = VisibleDescriptorKinds.ToArray()
    };
}

public sealed record AgentMemoryVisibilityBoundary
{
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public bool AllowUnscopedMemory { get; init; }

    public AgentMemoryVisibilityBoundary Snapshot() => this with
    {
        VisibleDescriptorRefs = VisibleDescriptorRefs.ToArray()
    };
}

public enum AgentMemoryOperationFailureCode
{
    Unknown = 0,
    ResourceUnavailable = 1,
    InvalidLifecycleState = 2,
    TenantMismatch = 3,
    MissingActor = 4,
    MissingReason = 5,
    MissingTimestamp = 6,
    MissingSourceOrExplanation = 7,
    StateConflict = 8,
    IdentityConflict = 9
}

public sealed class AgentMemoryOperationException : InvalidOperationException
{
    public AgentMemoryOperationException(
        AgentMemoryOperationFailureCode code,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public AgentMemoryOperationFailureCode Code { get; }
}

public sealed record AgentMemoryCandidateExpectation
{
    public required string CandidateId { get; init; }
    public required CanonicalHash ExpectedStateHash { get; init; }
}

public sealed record AgentMemoryItemExpectation
{
    public required string MemoryId { get; init; }
    public required CanonicalHash ExpectedStateHash { get; init; }
}

public sealed record AgentMemoryPromotionPlan
{
    public required AgentMemoryCandidateExpectation Candidate { get; init; }
    public required string NewMemoryId { get; init; }
    public required CanonicalHash ExpectedMemoryContentHash { get; init; }
    public required CanonicalHash ExpectedMemoryStateHash { get; init; }
    public required AgentMemoryOperationRequest Operation { get; init; }
}

public sealed record AgentMemorySupersessionPlan
{
    public required AgentMemoryItemExpectation TargetMemory { get; init; }
    public required AgentMemoryCandidateExpectation ReplacementCandidate { get; init; }
    public required string NewMemoryId { get; init; }
    public required CanonicalHash ExpectedMemoryContentHash { get; init; }
    public required CanonicalHash ExpectedMemoryStateHash { get; init; }
    public required AgentMemoryOperationRequest Operation { get; init; }
}

public sealed record AgentMemoryPack : ISnapshotable<AgentMemoryPack>
{
    public required string TenantId { get; init; }
    public IReadOnlyList<AgentMemoryItem> Memories { get; init; } = Array.Empty<AgentMemoryItem>();
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();
    public bool IsAuthoritative { get; init; }
    public bool WasTruncated { get; init; }
    public CanonicalHash? ScopeFingerprint { get; init; }
    public CanonicalHash? VisibleMemorySetHash { get; init; }
    public CanonicalHash? CanonicalPackHash { get; init; }

    public AgentMemoryPack Snapshot() => this with
    {
        Memories = Memories.Select(item => item.Snapshot()).ToArray(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentMemoryOperationRequest : ISnapshotable<AgentMemoryOperationRequest>
{
    public required string TenantId { get; init; }
    public required AgentMemoryInvocationContext InvocationContext { get; init; }
    public required string Reason { get; init; }
    public required AgentMemoryOperationIdentity Identity { get; init; }
    public IReadOnlyList<AgentContextSourceRef> SourceRefs { get; init; } = Array.Empty<AgentContextSourceRef>();
    public string? Explanation { get; init; }

    public AgentMemoryOperationRequest Snapshot() => this with
    {
        InvocationContext = InvocationContext.Snapshot(),
        SourceRefs = SourceRefs.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentSourceExpansionResult : ISnapshotable<AgentSourceExpansionResult>
{
    public required AgentContextSourceRef SourceRef { get; init; }
    public required AgentMemorySourceExpansionStatus Status { get; init; }
    public string? SanitizedContent { get; init; }
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public AgentSourceExpansionResult Snapshot() => this with
    {
        SourceRef = SourceRef.Snapshot(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}

public sealed record AgentAuthoringRequest : ISnapshotable<AgentAuthoringRequest>
{
    public required string TenantId { get; init; }
    public required string IntentText { get; init; }
    public AgentMemoryQuery? MemoryQuery { get; init; }

    public AgentAuthoringRequest Snapshot() => this with
    {
        MemoryQuery = MemoryQuery?.Copy()
    };
}

public sealed record AgentAuthoringContext : ISnapshotable<AgentAuthoringContext>
{
    public required AgentAuthoringRequest Request { get; init; }
    public required MetadataContextPack MetadataContextPack { get; init; }
    public required AgentMemoryPack MemoryPack { get; init; }
    public IReadOnlyList<AgentMemoryDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentMemoryDiagnostic>();

    public AgentAuthoringContext Snapshot() => this with
    {
        Request = Request.Snapshot(),
        MetadataContextPack = MetadataContextPack.Copy(),
        MemoryPack = MemoryPack.Snapshot(),
        Diagnostics = Diagnostics.Select(item => item.Snapshot()).ToArray()
    };
}
