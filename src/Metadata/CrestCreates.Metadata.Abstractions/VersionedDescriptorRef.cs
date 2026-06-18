namespace CrestCreates.Metadata.Abstractions;

public readonly record struct VersionedDescriptorRef<TDescriptor>(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null
) where TDescriptor : IVersionedDescriptor;
