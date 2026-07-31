using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryDescriptorSnapshotStore : IDescriptorSnapshotStore
{
    private readonly Dictionary<string, (DescriptorSnapshot Snapshot, string Fingerprint)> _snapshots = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public Task<DescriptorSnapshotWriteResult> WriteAsync(DescriptorSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var copy = snapshot.Snapshot();
        var fingerprint = Fingerprint(copy);
        lock (_gate)
        {
            if (_snapshots.TryGetValue(copy.SnapshotId, out var existing))
                return Task.FromResult(new DescriptorSnapshotWriteResult(existing.Fingerprint == fingerprint ? DescriptorSnapshotWriteStatus.Duplicate : DescriptorSnapshotWriteStatus.Conflict, copy.SnapshotId));
            _snapshots.Add(copy.SnapshotId, (copy, fingerprint));
            return Task.FromResult(new DescriptorSnapshotWriteResult(DescriptorSnapshotWriteStatus.Accepted, copy.SnapshotId));
        }
    }
    public Task<DescriptorSnapshot?> GetAsync(string snapshotId, CancellationToken cancellationToken = default)
    { lock (_gate) return Task.FromResult(_snapshots.TryGetValue(snapshotId, out var value) ? value.Snapshot.Snapshot() : null); }
    public Task<SnapshotEntry?> GetEntryAsync(string snapshotId, DescriptorRef descriptorRef, CancellationToken cancellationToken = default)
    { lock (_gate) return Task.FromResult(_snapshots.TryGetValue(snapshotId, out var value) ? value.Snapshot.Descriptors.FirstOrDefault(e => e.Ref == descriptorRef)?.Snapshot() : null); }
    private static string Fingerprint(DescriptorSnapshot snapshot) => string.Join("|", snapshot.SnapshotId, snapshot.PackageId, snapshot.PackageVersion, string.Join(";", snapshot.Descriptors.Select(e => $"{e.Ref.Namespace}:{e.Ref.Id}:{e.Ref.Version}:{e.ContractHash}:{e.DefinitionHash}")), string.Join(";", snapshot.Relationships));
}
