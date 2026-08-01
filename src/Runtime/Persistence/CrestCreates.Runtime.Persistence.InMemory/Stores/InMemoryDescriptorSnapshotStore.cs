using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryDescriptorSnapshotStore : IDescriptorSnapshotStore
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;

    public InMemoryDescriptorSnapshotStore(InMemoryRuntimeTransactionCoordinator coordinator)
        => _coordinator = coordinator;
    public Task<DescriptorSnapshotWriteResult> WriteAsync(DescriptorSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var copy = snapshot.Snapshot();
        var fingerprint = Fingerprint(copy);
        return _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(WriteCore(copy, fingerprint)); }, cancellationToken).AsTask();
    }

    public Task<DescriptorSnapshot?> GetAsync(string snapshotId, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(_coordinator.CurrentState.Snapshots.TryGetValue(snapshotId, out var value) ? value.Snapshot.Snapshot() : null); }, cancellationToken).AsTask();

    public Task<SnapshotEntry?> GetEntryAsync(string snapshotId, DescriptorRef descriptorRef, CancellationToken cancellationToken = default)
        => _coordinator.ExecuteAsync(_ => { using var guard = _coordinator.EnterStoreOperation(); return ValueTask.FromResult(_coordinator.CurrentState.Snapshots.TryGetValue(snapshotId, out var value) ? value.Snapshot.Descriptors.FirstOrDefault(e => e.Ref == descriptorRef)?.Snapshot() : null); }, cancellationToken).AsTask();

    private DescriptorSnapshotWriteResult WriteCore(DescriptorSnapshot copy, string fingerprint)
    {
        if (_coordinator.CurrentState.Snapshots.TryGetValue(copy.SnapshotId, out var existing))
        {
            return new DescriptorSnapshotWriteResult(
                existing.Fingerprint == fingerprint ? DescriptorSnapshotWriteStatus.Duplicate : DescriptorSnapshotWriteStatus.Conflict,
                copy.SnapshotId);
        }
        _coordinator.CurrentState.Snapshots.Add(copy.SnapshotId, (copy, fingerprint));
        return new DescriptorSnapshotWriteResult(DescriptorSnapshotWriteStatus.Accepted, copy.SnapshotId);
    }
    private static string Fingerprint(DescriptorSnapshot snapshot) => string.Join("|", snapshot.SnapshotId, snapshot.PackageId, snapshot.PackageVersion, string.Join(";", snapshot.Descriptors.Select(e => $"{e.Ref.Namespace}:{e.Ref.Id}:{e.Ref.Version}:{e.ContractHash}:{e.DefinitionHash}")), string.Join(";", snapshot.Relationships));
}
