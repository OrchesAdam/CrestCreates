namespace CrestCreates.Agent.Memory.Accountability;

/// <summary>
/// Frozen wire kinds and version for the Agent Memory accountability payloads.
/// These constants are the single source of truth shared by the producer, the
/// sanitization rules, and the AuditId projection. Do not derive them from CLR
/// record names or enum.ToString().
/// </summary>
public static class AgentMemoryAccountabilityPayloadKinds
{
    public const string Recall = "agent-memory.recall.result";
    public const string Curation = "agent-memory.curation.result";
    public const string SourceExpansion = "agent-memory.source-expansion.result";

    public const int Version = 1;

    // Accountability v1 hard limits plus Memory-specific lower bounds (spec §11).
    public const int MaxDiagnosticCodes = 32;
    public const int MaxRedactionCodes = 16;
    public const int MaxRequestedKinds = 6;
    public const int MaxIdentifierLength = 256;
    public const int MaxCodeLength = 128;
    public const int MaxPayloadVersion = 1;

    public const string EffectiveContentArtifactKind = "AgentMemoryAccountabilityEffectiveVisibleContent";
    public const string EffectiveContentPurpose = "AuditEvidence";
    public const string EffectiveContentScope = "TenantVisible";
    public const string EffectiveContentContractVersion = "agent-memory-accountability-effective-content-v1";
    public const string EffectiveContentCanonicalShapeVersion = "agent-memory-accountability-effective-content-v1";
    public const string EffectivePackArtifactKind = "AgentMemoryAccountabilityEffectivePack";
    public const string EffectivePackPurpose = "AuditEvidence";
    public const string EffectivePackScope = "TenantVisible";
    public const string EffectivePackContractVersion = "agent-memory-accountability-effective-pack-v1";
    public const string EffectivePackCanonicalShapeVersion = "agent-memory-accountability-effective-pack-v1";

    public static IReadOnlySet<string> RequestedKindAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Preference", "ProjectFact", "Decision", "Constraint", "WorkflowHint", "Risk"
        };

    public static IReadOnlySet<string> MinimumConfidenceAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "0.0", "0.3", "0.5", "0.8" };

    public static IReadOnlySet<string> SourceKindAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ConversationTurn", "TaskRecord", "TaskEvent", "CompressedContextBlock",
            "MemoryCandidate", "MemoryItem"
        };

    // These are the only stable codes emitted by the current Memory
    // sanitization/domain contracts.  Accountability must not become a
    // free-form diagnostic channel: provider/user text is rejected even when
    // it is placed in a field named "Code".
    public static IReadOnlySet<string> DiagnosticCodeAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "AGENT_MEMORY_BLOCK_SANITIZED",
            "AGENT_MEMORY_CONTENT_REDACTED",
            "AGENT_MEMORY_CONTENT_REJECTED",
            "AGENT_MEMORY_EMPTY_CONTENT",
            "AGENT_MEMORY_SOURCE_NOT_FOUND",
            "AGENT_MEMORY_SOURCE_NOT_EXPANDABLE",
            "AGENT_MEMORY_BUDGET_TRUNCATED",
            "AGENT_MEMORY_VISIBILITY_KIND_UNRESOLVABLE",
            "AGENT_MEMORY_LLM_BLOCK_COUNT_TRUNCATED",
            "AGENT_MEMORY_LLM_BLOCK_TRUNCATED",
            "AGENT_MEMORY_LLM_CANDIDATE_COUNT_TRUNCATED",
            "AGENT_MEMORY_LLM_CANDIDATE_TRUNCATED",
            "AGENT_MEMORY_LLM_CONTENT_REJECTED",
            "AGENT_MEMORY_LLM_REDACTION_OCCURRED",
            "AGENT_MEMORY_LLM_SOURCE_REF_MISSING",
            "AGENT_MEMORY_LLM_PROVIDER_UNAVAILABLE",
            "AGENT_MEMORY_LLM_CREDENTIAL_UNAVAILABLE",
            "AGENT_MEMORY_LLM_UNAUTHORIZED",
            "AGENT_MEMORY_LLM_RATE_LIMITED",
            "AGENT_MEMORY_LLM_TIMEOUT",
            "AGENT_MEMORY_LLM_NETWORK_ERROR",
            "AGENT_MEMORY_LLM_PROVIDER_RETURNED_EMPTY_OUTPUT",
            "AGENT_MEMORY_LLM_PARSE_FAILED",
            "AGENT_MEMORY_LLM_INVALID_SOURCE_REF",
            "AGENT_MEMORY_LLM_FALLBACK_TO_DETERMINISTIC_COMPRESSOR",
            "AGENT_MEMORY_LLM_FALLBACK_TO_DETERMINISTIC_EXTRACTOR",
            "AGENT_MEMORY_LLM_NON_AUTHORITATIVE_OUTPUT_ENFORCED",
            "AGENT_MEMORY_LLM_CANDIDATE_CONFIDENCE_CAPPED",
            "AGENT_MEMORY_LLM_COMPRESSION_PARSE_ERROR",
            "AGENT_MEMORY_LLM_EXTRACTION_PARSE_ERROR"
        };

    public static IReadOnlySet<string> RedactionCodeAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "empty-content",
            "bearer-token",
            "credential",
            "connection-credential",
            "long-token"
        };

    public static IReadOnlySet<string> RecallFailureCodeAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "budget-invalid", "resource-unavailable" };

    public static IReadOnlySet<string> CurationRejectedCodeAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "resource-unavailable", "invalid-lifecycle-state", "tenant-mismatch",
            "missing-actor", "missing-reason", "missing-timestamp", "missing-source-or-explanation"
        };

    public static IReadOnlySet<string> CurationConflictCodeAllowList { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "state-conflict", "identity-conflict" };
}
