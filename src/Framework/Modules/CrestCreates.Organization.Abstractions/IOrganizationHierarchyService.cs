namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationHierarchyService
{
    Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsDescendantOfAsync(string orgUnitId, string ancestorOrgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsUserInOrganizationAsync(string userId, string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> IsUserInDescendantOrganizationAsync(string userId, string ancestorOrganizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);
}
