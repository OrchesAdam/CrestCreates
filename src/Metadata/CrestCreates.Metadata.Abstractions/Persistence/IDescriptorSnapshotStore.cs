namespace CrestCreates.Metadata.Abstractions.Persistence;

public interface IDescriptorSnapshotStore
{
    Task<DescriptorSnapshotWriteResult> WriteAsync(
        DescriptorSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<DescriptorSnapshot?> GetAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    Task<SnapshotEntry?> GetEntryAsync(
        string snapshotId,
        DescriptorRef descriptorRef,
        CancellationToken cancellationToken = default);
}
