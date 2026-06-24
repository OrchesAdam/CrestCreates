namespace CrestCreates.Metadata.Abstractions.Evidence;

public sealed record EvidenceFinding
{
    public required string Source { get; init; }
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public DescriptorRef? Subject { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; } = Array.Empty<DescriptorRef>();
}
