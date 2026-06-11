using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultOrganizationIdentityService : IOrganizationIdentityService
{
    private readonly IOrganizationStore _store;

    public DefaultOrganizationIdentityService(IOrganizationStore store)
    {
        _store = store;
    }

    public async Task<OrganizationContext> GetContextAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        var activeMemberships = memberships.Where(m => m.IsActive).ToList();

        var primary = activeMemberships
            .Where(m => m.IsPrimary)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefault();

        var roleAssignments = await _store.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
        var activeRoles = roleAssignments.Where(r => r.IsActive).ToList();

        return new OrganizationContext
        {
            TenantId = tenantId,
            UserId = userId,
            PrimaryOrganizationUnitId = primary?.OrganizationUnitId,
            OrganizationUnitIds = activeMemberships.Select(m => m.OrganizationUnitId).Distinct().ToList().AsReadOnly(),
            RoleIds = activeRoles.Select(r => r.RoleId).Distinct().ToList().AsReadOnly(),
            PositionIds = activeMemberships.Where(m => m.PositionId is not null).Select(m => m.PositionId!).Distinct().ToList().AsReadOnly()
        };
    }

    public async Task<bool> IsInRoleAsync(string userId, string roleId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var assignments = await _store.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
        return assignments.Any(a => a.IsActive && a.RoleId == roleId);
    }

    public async Task<bool> HasPositionAsync(string userId, string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Any(m => m.IsActive && m.PositionId == positionId);
    }

    public async Task<IReadOnlyList<string>> GetUserOrganizationUnitIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Where(m => m.IsActive).Select(m => m.OrganizationUnitId).Distinct().ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> GetUserRoleIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var assignments = await _store.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
        return assignments.Where(a => a.IsActive).Select(a => a.RoleId).Distinct().ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> GetUserPositionIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Where(m => m.IsActive && m.PositionId is not null).Select(m => m.PositionId!).Distinct().ToList().AsReadOnly();
    }
}
