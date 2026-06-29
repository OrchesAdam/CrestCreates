using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Abstractions;

public static class AgentMemoryDiagnosticCodes
{
    private const string EmptyContentValue = "AGENT_MEMORY_EMPTY_CONTENT";
    public static DiagnosticCode EmptyContent { get; } = new(EmptyContentValue);

    private const string SourceNotFoundValue = "AGENT_MEMORY_SOURCE_NOT_FOUND";
    public static DiagnosticCode SourceNotFound { get; } = new(SourceNotFoundValue);

    private const string SourceNotExpandableValue = "AGENT_MEMORY_SOURCE_NOT_EXPANDABLE";
    public static DiagnosticCode SourceNotExpandable { get; } = new(SourceNotExpandableValue);

    private const string ContentRedactedValue = "AGENT_MEMORY_CONTENT_REDACTED";
    public static DiagnosticCode ContentRedacted { get; } = new(ContentRedactedValue);

    private const string ContentRejectedValue = "AGENT_MEMORY_CONTENT_REJECTED";
    public static DiagnosticCode ContentRejected { get; } = new(ContentRejectedValue);

    private const string BlockSanitizedValue = "AGENT_MEMORY_BLOCK_SANITIZED";
    public static DiagnosticCode BlockSanitized { get; } = new(BlockSanitizedValue);

    private const string BudgetTruncatedValue = "AGENT_MEMORY_BUDGET_TRUNCATED";
    public static DiagnosticCode BudgetTruncated { get; } = new(BudgetTruncatedValue);

    private const string InvalidOperationTenantMismatchValue = "AGENT_MEMORY_INVALID_OPERATION_TENANT_MISMATCH";
    public static DiagnosticCode InvalidOperationTenantMismatch { get; } = new(InvalidOperationTenantMismatchValue);

    private const string InvalidOperationMissingActorValue = "AGENT_MEMORY_INVALID_OPERATION_MISSING_ACTOR";
    public static DiagnosticCode InvalidOperationMissingActor { get; } = new(InvalidOperationMissingActorValue);

    private const string InvalidOperationMissingReasonValue = "AGENT_MEMORY_INVALID_OPERATION_MISSING_REASON";
    public static DiagnosticCode InvalidOperationMissingReason { get; } = new(InvalidOperationMissingReasonValue);

    private const string InvalidOperationMissingTimestampValue = "AGENT_MEMORY_INVALID_OPERATION_MISSING_TIMESTAMP";
    public static DiagnosticCode InvalidOperationMissingTimestamp { get; } = new(InvalidOperationMissingTimestampValue);

    private const string InvalidOperationMissingSourceOrExplanationValue = "AGENT_MEMORY_INVALID_OPERATION_MISSING_SOURCE_OR_EXPLANATION";
    public static DiagnosticCode InvalidOperationMissingSourceOrExplanation { get; } = new(InvalidOperationMissingSourceOrExplanationValue);

    private const string VisibilityKindUnresolvableValue = "AGENT_MEMORY_VISIBILITY_KIND_UNRESOLVABLE";
    public static DiagnosticCode VisibilityKindUnresolvable { get; } = new(VisibilityKindUnresolvableValue);

    public static class AgentMemoryRedactionKinds
    {
        public const string EmptyContent = "empty-content";
        public const string BearerToken = "bearer-token";
        public const string Credential = "credential";
        public const string ConnectionCredential = "connection-credential";
        public const string LongToken = "long-token";
    }
}
