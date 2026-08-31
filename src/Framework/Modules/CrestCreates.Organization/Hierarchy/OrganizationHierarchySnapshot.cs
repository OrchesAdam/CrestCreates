using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal sealed record OrganizationHierarchyCacheKey(
    string TenantId,
    long Generation);

internal sealed record OrganizationHierarchySnapshot(
    long Generation,
    IReadOnlyList<OrganizationUnit> Units,
    IReadOnlyDictionary<OrganizationScopedKey, OrganizationUnit> UnitMap,
    IReadOnlyDictionary<OrganizationScopedKey, IReadOnlyList<OrganizationScopedKey>> ChildrenMap);
