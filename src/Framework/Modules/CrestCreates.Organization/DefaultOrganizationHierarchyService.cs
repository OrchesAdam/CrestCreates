using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultOrganizationHierarchyService : IOrganizationHierarchyService
{
    private readonly IOrganizationStore _store;

    public DefaultOrganizationHierarchyService(IOrganizationStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var allUnits = await _store.GetOrganizationUnitsAsync(tenantId, cancellationToken: cancellationToken);
        var unitMap = allUnits.ToDictionary(
            u => OrganizationScopedKey.FromTenantId(u.TenantId, u.Id));
        var result = new List<OrganizationUnit>();
        var visited = new HashSet<string> { organizationUnitId };
        var currentId = organizationUnitId;

        while (true)
        {
            var lookupKey = OrganizationScopedKey.FromTenantId(tenantId, currentId);
            if (!unitMap.TryGetValue(lookupKey, out var current))
                break;

            var parentId = current.ParentId;
            if (parentId is null)
                break;

            if (!visited.Add(parentId))
                throw new OrganizationHierarchyException(
                    $"Circular hierarchy detected: organization unit '{parentId}' is already in the ancestor chain starting from '{organizationUnitId}'.");

            var parentKey = OrganizationScopedKey.FromTenantId(tenantId, parentId);
            if (!unitMap.TryGetValue(parentKey, out var parent))
                break;

            result.Add(parent.Snapshot());
            currentId = parentId;
        }

        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var allUnits = await _store.GetOrganizationUnitsAsync(tenantId, cancellationToken: cancellationToken);
        var unitMap = allUnits.ToDictionary(
            u => OrganizationScopedKey.FromTenantId(u.TenantId, u.Id));
        var childrenMap = allUnits
            .Where(u => u.ParentId is not null)
            .GroupBy(u => OrganizationScopedKey.FromTenantId(u.TenantId, u.ParentId!))
            .ToDictionary(g => g.Key, g => g.Select(c => OrganizationScopedKey.FromTenantId(c.TenantId, c.Id)).ToList());

        var result = new List<OrganizationUnit>();
        var visited = new HashSet<OrganizationScopedKey> { OrganizationScopedKey.FromTenantId(tenantId, organizationUnitId) };
        var queue = new Queue<OrganizationScopedKey>();

        var startKey = OrganizationScopedKey.FromTenantId(tenantId, organizationUnitId);
        if (childrenMap.TryGetValue(startKey, out var directChildren))
        {
            foreach (var childKey in directChildren)
            {
                queue.Enqueue(childKey);
            }
        }

        while (queue.Count > 0)
        {
            var currentKey = queue.Dequeue();

            if (!visited.Add(currentKey))
                throw new OrganizationHierarchyException(
                    $"Circular hierarchy detected: organization unit '{currentKey}' appears multiple times in the descendant tree of '{organizationUnitId}'.");

            if (!unitMap.TryGetValue(currentKey, out var current))
                continue;

            result.Add(current.Snapshot());

            if (childrenMap.TryGetValue(currentKey, out var children))
            {
                foreach (var childKey in children)
                {
                    queue.Enqueue(childKey);
                }
            }
        }

        return result.AsReadOnly();
    }

    public async Task<bool> IsDescendantOfAsync(string organizationUnitId, string ancestorOrganizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        if (organizationUnitId == ancestorOrganizationUnitId)
            return false;

        var ancestors = await GetAncestorsAsync(organizationUnitId, tenantId, cancellationToken);
        return ancestors.Any(a => a.Id == ancestorOrganizationUnitId);
    }

    public async Task<bool> IsUserInOrganizationAsync(string userId, string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Any(m => m.IsActive && m.OrganizationUnitId == organizationUnitId);
    }

    public async Task<bool> IsUserInDescendantOrganizationAsync(string userId, string ancestorOrganizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        var activeMembershipOrgIds = memberships.Where(m => m.IsActive).Select(m => m.OrganizationUnitId).ToHashSet();

        if (activeMembershipOrgIds.Count == 0)
            return false;

        if (activeMembershipOrgIds.Contains(ancestorOrganizationUnitId))
            return true;

        var descendants = await GetDescendantsAsync(ancestorOrganizationUnitId, tenantId, cancellationToken);
        var descendantIds = descendants.Select(d => d.Id).ToHashSet();
        return activeMembershipOrgIds.Overlaps(descendantIds);
    }
}
