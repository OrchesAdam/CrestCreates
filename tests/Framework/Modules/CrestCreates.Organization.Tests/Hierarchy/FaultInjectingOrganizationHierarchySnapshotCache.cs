namespace CrestCreates.Organization.Tests.Hierarchy;

internal sealed class FaultInjectingOrganizationHierarchySnapshotCache : IOrganizationHierarchySnapshotCache
{
    private readonly Dictionary<OrganizationHierarchyCacheKey, OrganizationHierarchySnapshot> _entries = [];

    internal bool ThrowOnLookup { get; set; }
    internal bool ThrowOnSet { get; set; }
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
            throw new InvalidOperationException("injected snapshot publication failure");
        _entries[key] = snapshot;
    }

    public void Dispose() => _entries.Clear();
}
