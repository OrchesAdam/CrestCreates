using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

[Obsolete("Use IDescriptorPackageBuilder.Build() instead. This static method reads from " +
          "IGlobalDescriptorRegistry and does not produce deterministic snapshots.")]
public static class DescriptorSnapshotBuilder
{
    public static DescriptorSnapshot TakeSnapshot(
        IGlobalDescriptorRegistry registry,
        string packageId,
        string packageVersion,
        IDescriptorStableHashBuilder hashBuilder)
    {
        var allDescriptors = registry.GetAll();
        var entries = allDescriptors.Select(d =>
        {
            var hashes = hashBuilder.Build(d);
            return new SnapshotEntry
            {
                Ref = new DescriptorRef(d.Namespace, d.Id, (d as IVersionedDescriptor)?.Version),
                DescriptorName = d.Name,
                Kind = d.Kind,
                State = d.State,
                ContractHash = hashes.ContractHash.Value,
                DefinitionHash = hashes.DefinitionHash.Value,
                SupersededById = d.SupersededById
            };
        }).ToList();

        return new DescriptorSnapshot
        {
            SnapshotId = $"snapshot_{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
            PackageId = packageId,
            PackageVersion = packageVersion,
            Descriptors = entries
        };
    }
}