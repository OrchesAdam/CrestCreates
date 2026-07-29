using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditEnvelope
{
    public int ContractVersion { get; init; } = 1;
    public required string AuditId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? TenantId { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? ParentAuditId { get; init; }
    public string? PreviousAuditId { get; init; }
    public required AuditActor Actor { get; init; }
    public required AuditAction Action { get; init; }
    public required AuditTarget Target { get; init; }
    public required AuditOutcome Outcome { get; init; }
    public AuditRuntimeContext Runtime { get; init; } = AuditRuntimeContext.Empty;
    public AuditDescriptorContext Descriptors { get; init; } = AuditDescriptorContext.Empty;
    public AuditDataSnapshot? DataSnapshot { get; init; }
    public ImmutableArray<AuditEvidenceReference> Evidence { get; init; } = [];
    public AuditPayload? Payload { get; init; }
    public ImmutableSortedDictionary<string, string> Tags { get; init; } = AuditTagMap.Empty;
    public AuditSanitizationStamp? Sanitization { get; init; }
    public CanonicalHash? Integrity { get; init; }
}
