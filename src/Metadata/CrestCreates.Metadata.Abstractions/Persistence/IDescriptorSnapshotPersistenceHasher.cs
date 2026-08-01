namespace CrestCreates.Metadata.Abstractions.Persistence;

public interface IDescriptorSnapshotPersistenceHasher
{
    DescriptorSnapshotPersistenceHash Compute(DescriptorSnapshot snapshot);
}
