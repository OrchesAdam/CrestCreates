using System.Collections.Immutable;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditDataSnapshot
{
    public required string CapturePolicyId { get; init; }
    public required int CapturePolicyVersion { get; init; }
    public ImmutableArray<AuditDataArtifact> Artifacts { get; init; } = [];
}

public sealed record AuditDataArtifact
{
    public required string Kind { get; init; }
    public CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash? ContentHash { get; init; }
    public AuditDataHashBasis? ContentHashBasis { get; init; }
    public System.Text.Json.JsonElement? SanitizedValue { get; init; }
}

public enum AuditDataHashBasis
{
    Source = 1,
    Sanitized = 2
}
