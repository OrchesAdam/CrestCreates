namespace CrestCreates.Metadata.Abstractions;

public readonly record struct DescriptorKey(
    string Namespace,
    string Id,
    int Version);
