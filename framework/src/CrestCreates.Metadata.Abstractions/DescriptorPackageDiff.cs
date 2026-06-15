namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageDiff
{
    public required IReadOnlyList<DescriptorRef> AddedRefs { get; init; }
    public required IReadOnlyList<DescriptorRef> RemovedRefs { get; init; }
    public required IReadOnlyList<DescriptorDiffEntry> ChangedEntries { get; init; }
    public required IReadOnlyList<DescriptorStateChange> StateChanges { get; init; }
    public required IReadOnlyList<DescriptorPackageMetadataChange> MetadataChanges { get; init; }
    public string BeforeContentHash { get; init; } = string.Empty;
    public string AfterContentHash { get; init; } = string.Empty;
}

public sealed record DescriptorDiffEntry
{
    public required DescriptorRef Ref { get; init; }
    public string BeforeContractHash { get; init; } = string.Empty;
    public string AfterContractHash { get; init; } = string.Empty;
}

public sealed record DescriptorStateChange
{
    public required DescriptorRef Ref { get; init; }
    public DescriptorState FromState { get; init; }
    public DescriptorState ToState { get; init; }
}
