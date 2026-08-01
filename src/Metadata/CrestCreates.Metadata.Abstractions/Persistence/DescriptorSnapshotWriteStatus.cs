namespace CrestCreates.Metadata.Abstractions.Persistence;

public enum DescriptorSnapshotWriteStatus
{
    Accepted = 0,
    Duplicate = 1,
    Conflict = 2
}
