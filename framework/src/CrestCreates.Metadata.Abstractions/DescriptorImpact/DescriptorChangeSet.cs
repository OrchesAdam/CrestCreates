namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorChangeSet
{
    public required IReadOnlyList<DescriptorChange> Changes { get; init; }
}
