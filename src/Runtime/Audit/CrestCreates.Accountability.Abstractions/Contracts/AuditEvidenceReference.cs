using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditEvidenceReference
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public CanonicalHash? Hash { get; init; }
}
