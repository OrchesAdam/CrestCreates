namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationStore
{
    Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default);
    Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    Task SavePositionAsync(Position position, CancellationToken cancellationToken = default);
    Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);

    Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);

    Task<OrganizationScopeGenerationRead> ReadScopeGenerationAsync(
        OrganizationScopeIdentity scope,
        CancellationToken cancellationToken = default);
}
