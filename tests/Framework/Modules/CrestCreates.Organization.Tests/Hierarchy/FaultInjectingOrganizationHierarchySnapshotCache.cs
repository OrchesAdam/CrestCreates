namespace CrestCreates.Organization.Tests.Hierarchy;

internal sealed class FaultInjectingOrganizationHierarchySnapshotCache : IOrganizationHierarchySnapshotCache
{
    private readonly Dictionary<OrganizationHierarchyCacheKey, OrganizationHierarchySnapshot> _entries = [];

    internal bool ThrowOnLookup { get; set; }
    internal bool ThrowOnSet { get; set; }
    internal bool WriteBeforeThrow { get; set; }
    internal Action? BeforeSet { get; set; }
    internal int SetCount { get; private set; }

    public bool TryGet(OrganizationHierarchyCacheKey key, out OrganizationHierarchySnapshot snapshot)
    {
        if (ThrowOnLookup)
            throw new InvalidOperationException("injected snapshot lookup failure");
        return _entries.TryGetValue(key, out snapshot!);
    }

    public void Set(OrganizationHierarchyCacheKey key, OrganizationHierarchySnapshot snapshot)
    {
        SetCount++;
        BeforeSet?.Invoke();
        if (ThrowOnSet)
        {
            if (WriteBeforeThrow)
                _entries[key] = snapshot;
            throw new InvalidOperationException("injected snapshot publication failure");
        }
        _entries[key] = snapshot;
    }

    public bool Remove(OrganizationHierarchyCacheKey key, OrganizationHierarchySnapshot expectedSnapshot)
    {
        if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, expectedSnapshot))
            return false;
        return _entries.Remove(key);
    }

    public void Dispose() => _entries.Clear();
}
