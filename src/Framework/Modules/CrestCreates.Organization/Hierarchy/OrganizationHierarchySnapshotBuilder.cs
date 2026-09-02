using System.Collections.Immutable;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal sealed class OrganizationHierarchySnapshotBuilder
{
    public static OrganizationHierarchySnapshot Build(
        long generation,
        IReadOnlyList<OrganizationUnit> units)
    {
        var canonicalUnits = units
            .Select(unit => unit.Snapshot())
            .OrderBy(unit => unit, OrganizationStoreSemantics.OrganizationUnitComparer)
            .ToArray();
        var unitMap = new Dictionary<OrganizationScopedKey, OrganizationUnit>();
        foreach (var unit in canonicalUnits)
        {
            var key = OrganizationScopedKey.FromTenantId(unit.TenantId, unit.Id);
            unitMap[key] = unit;
        }

        var childrenMap = new Dictionary<OrganizationScopedKey, List<OrganizationScopedKey>>();
        foreach (var unit in canonicalUnits)
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

        // Preserve the canonical Store order inside each breadth-first level.
        var immutableChildrenMap = new Dictionary<OrganizationScopedKey, IReadOnlyList<OrganizationScopedKey>>();
        foreach (var (key, children) in childrenMap)
        {
            immutableChildrenMap[key] = children.AsReadOnly();
        }

        return new OrganizationHierarchySnapshot(
            generation,
            canonicalUnits,
            unitMap.ToImmutableDictionary(),
            immutableChildrenMap.ToImmutableDictionary());
    }
}
