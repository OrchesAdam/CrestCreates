namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationIdentityService
{
    Task<OrganizationContext> GetContextAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> IsInRoleAsync(string userId, string roleId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> HasPositionAsync(string userId, string positionId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserOrganizationUnitIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserRoleIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserPositionIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
}
