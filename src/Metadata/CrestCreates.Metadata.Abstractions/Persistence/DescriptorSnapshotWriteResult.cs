namespace CrestCreates.Metadata.Abstractions.Persistence;

public sealed record DescriptorSnapshotWriteResult(
    DescriptorSnapshotWriteStatus Status,
    string SnapshotId);
