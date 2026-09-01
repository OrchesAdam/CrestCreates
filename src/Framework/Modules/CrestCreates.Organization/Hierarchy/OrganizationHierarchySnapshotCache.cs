using Microsoft.Extensions.Caching.Memory;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal sealed class OrganizationHierarchySnapshotCacheException : OrganizationException
{
    public OrganizationHierarchySnapshotCacheException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal interface IOrganizationHierarchySnapshotCache : IDisposable
{
    bool TryGet(OrganizationHierarchyCacheKey key, out OrganizationHierarchySnapshot snapshot);

    void Set(OrganizationHierarchyCacheKey key, OrganizationHierarchySnapshot snapshot);
}

internal sealed class MemoryOrganizationHierarchySnapshotCache : IOrganizationHierarchySnapshotCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _slidingExpiration;

    public MemoryOrganizationHierarchySnapshotCache(OrganizationHierarchyCacheOptions options)
    {
        _slidingExpiration = options.SnapshotSlidingExpiration;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.SnapshotCapacity,
            CompactionPercentage = 0.25
        });
    }

    public bool TryGet(OrganizationHierarchyCacheKey key, out OrganizationHierarchySnapshot snapshot)
        => _cache.TryGetValue(key, out snapshot!);

    public void Set(OrganizationHierarchyCacheKey key, OrganizationHierarchySnapshot snapshot)
    {
        _cache.Set(
            key,
            snapshot,
            new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetSlidingExpiration(_slidingExpiration));
    }

    public void Dispose() => _cache.Dispose();
}
