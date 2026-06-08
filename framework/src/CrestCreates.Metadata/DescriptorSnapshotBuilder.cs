using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorSnapshotBuilder
{
    public static DescriptorSnapshot TakeSnapshot(
        IGlobalDescriptorRegistry registry,
        string packageId,
        string packageVersion)
    {
        var allDescriptors = registry.GetAll();
        var entries = allDescriptors.Select(d => new SnapshotEntry
        {
            DescriptorId = d.Id,
            DescriptorName = d.Name,
            Kind = d.Kind,
            Version = (d as IVersionedDescriptor)?.Version ?? 0
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