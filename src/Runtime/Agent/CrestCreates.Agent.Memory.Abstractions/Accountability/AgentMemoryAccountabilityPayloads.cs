using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Abstractions.Accountability;

/// <summary>
/// Sanitization summary recorded for accountability. Arrays are ordinal-deduplicated,
/// sorted, and bounded before serialization; no RuleSet or RuleSetVersion is recorded.
/// </summary>
public sealed record AgentMemoryAccountabilitySanitizationSummary
{
    /// <summary>none | redacted | rejected</summary>
    public required string State { get; init; }

    public IReadOnlyList<string> RedactionCodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DiagnosticCodes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Accountability payload for a completed or deterministically rejected Recall.
/// v1; CLR record name does not define the wire identity.
/// </summary>
public sealed record AgentMemoryRecallAccountabilityPayload
{
    public required string OperationId { get; init; }

    /// <summary>completed | rejected</summary>
    public required string Result { get; init; }

    public string? StableFailureCode { get; init; }

    /// <summary>Required when Result is completed; null for deterministic pre-result rejection.</summary>
    public CanonicalHash? EffectivePackHash { get; init; }

    public required int ReturnedCount { get; init; }

    public required bool WasTruncated { get; init; }

    public IReadOnlyList<string> DiagnosticCodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RequestedKinds { get; init; } = Array.Empty<string>();

    public required int MaximumCount { get; init; }

    public required int CharacterBudget { get; init; }

    public required string MinimumConfidence { get; init; }
}

/// <summary>
/// Accountability payload for a committed, rejected, or conflicted curation
/// operation. Reason/Explanation never enter the payload.
/// </summary>
public sealed record AgentMemoryCurationAccountabilityPayload
{
    public required string OperationId { get; init; }

    /// <summary>promote | reject | supersede | archive</summary>
    public required string Operation { get; init; }

    public string? CandidateId { get; init; }

    public string? MemoryId { get; init; }

    public string? ReplacementCandidateId { get; init; }

    public string? NewMemoryId { get; init; }

    public CanonicalHash? ExpectedCandidateStateHash { get; init; }

    public CanonicalHash? ExpectedMemoryStateHash { get; init; }

    public CanonicalHash? ExpectedReplacementStateHash { get; init; }

    public CanonicalHash? ExpectedContentHash { get; init; }

    public string? PreviousState { get; init; }

    public string? ResultingState { get; init; }

    /// <summary>committed | rejected | conflict</summary>
    public required string Result { get; init; }

    public string? StableFailureCode { get; init; }

    public AgentMemoryAccountabilitySanitizationSummary? Sanitization { get; init; }
}

/// <summary>
/// Accountability payload for a Source Expansion attempt. GrantId is never recorded.
/// </summary>
public sealed record AgentMemorySourceExpansionAccountabilityPayload
{
    public required string OperationId { get; init; }

    public required string SourceKind { get; init; }

    /// <summary>Present only once a legal Grant has been observed.</summary>
    public required string SourceId { get; init; }

    public int? RangeStart { get; init; }

    public int? RangeEnd { get; init; }

    /// <summary>expanded | redacted | not-found | not-expandable | external-source-not-supported</summary>
    public required string Status { get; init; }

    /// <summary>Present only when Status is expanded.</summary>
    public CanonicalHash? EffectiveVisibleContentHash { get; init; }

    public required int MaximumCharacters { get; init; }

    public required bool WasTruncated { get; init; }

    public required AgentMemoryAccountabilitySanitizationSummary Sanitization { get; init; }

    public IReadOnlyList<string> DiagnosticCodes { get; init; } = Array.Empty<string>();
}
