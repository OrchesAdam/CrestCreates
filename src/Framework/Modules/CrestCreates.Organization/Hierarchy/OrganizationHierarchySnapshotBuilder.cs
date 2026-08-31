using System.Collections.Immutable;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal sealed class OrganizationHierarchySnapshotBuilder
{
    public static OrganizationHierarchySnapshot Build(
        long generation,
        IReadOnlyList<OrganizationUnit> units)
    {
        var unitMap = new Dictionary<OrganizationScopedKey, OrganizationUnit>();
        foreach (var unit in units)
        {
            var key = OrganizationScopedKey.FromTenantId(unit.TenantId, unit.Id);
            unitMap[key] = unit;
        }

        var childrenMap = new Dictionary<OrganizationScopedKey, List<OrganizationScopedKey>>();
        foreach (var unit in units)
        {
            if (unit.ParentId is not null)
            {
                var parentKey = OrganizationScopedKey.FromTenantId(unit.TenantId, unit.ParentId);
                if (!childrenMap.TryGetValue(parentKey, out var children))
                {
                    children = new List<OrganizationScopedKey>();
                    childrenMap[parentKey] = children;
                }
                children.Add(OrganizationScopedKey.FromTenantId(unit.TenantId, unit.Id));
            }
        }

        // Finalize with canonical comparers
        var immutableChildrenMap = new Dictionary<OrganizationScopedKey, IReadOnlyList<OrganizationScopedKey>>();
        foreach (var (key, children) in childrenMap)
        {
            children.Sort();
            immutableChildrenMap[key] = children.AsReadOnly();
        }

        return new OrganizationHierarchySnapshot(
            generation,
            units,
            unitMap.ToImmutableDictionary(),
            immutableChildrenMap.ToImmutableDictionary());
    }
}
