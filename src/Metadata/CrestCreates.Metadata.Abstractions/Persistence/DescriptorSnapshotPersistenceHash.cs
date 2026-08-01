namespace CrestCreates.Metadata.Abstractions.Persistence;

public sealed record DescriptorSnapshotPersistenceHash(
    string Algorithm,
    string Profile,
    string Digest);
