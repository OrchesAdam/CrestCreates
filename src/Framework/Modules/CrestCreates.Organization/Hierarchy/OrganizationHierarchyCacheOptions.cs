namespace CrestCreates.Organization;

internal sealed record OrganizationHierarchyCacheOptions
{
    public int SnapshotCapacity { get; }
    public TimeSpan SnapshotSlidingExpiration { get; }
    public int SafetyScopeCapacity { get; }
    public int PhysicalLoadCapacity { get; }
    public TimeSpan SharedLoadTimeout { get; }

    public OrganizationHierarchyCacheOptions(
        int snapshotCapacity = 1024,
        TimeSpan? snapshotSlidingExpiration = null,
        int safetyScopeCapacity = 16384,
        int physicalLoadCapacity = 2048,
        TimeSpan? sharedLoadTimeout = null)
    {
        if (snapshotCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshotCapacity));
        if (snapshotSlidingExpiration is not null && snapshotSlidingExpiration.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(snapshotSlidingExpiration));
        if (safetyScopeCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(safetyScopeCapacity));
        if (physicalLoadCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(physicalLoadCapacity));
        if (sharedLoadTimeout is not null && sharedLoadTimeout.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sharedLoadTimeout));

        SnapshotCapacity = snapshotCapacity;
        SnapshotSlidingExpiration = snapshotSlidingExpiration ?? TimeSpan.FromMinutes(15);
        SafetyScopeCapacity = safetyScopeCapacity;
        PhysicalLoadCapacity = physicalLoadCapacity;
        SharedLoadTimeout = sharedLoadTimeout ?? TimeSpan.FromSeconds(30);
    }
}
