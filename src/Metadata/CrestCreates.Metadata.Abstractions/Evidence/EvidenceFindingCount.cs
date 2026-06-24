namespace CrestCreates.Metadata.Abstractions.Evidence;

public sealed record EvidenceFindingCount
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public int Count { get; init; }
}
