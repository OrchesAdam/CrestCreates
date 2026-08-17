using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly ConcurrentDictionary<OrganizationScopedKey, OrganizationUnit> _orgUnits = new();
    private readonly ConcurrentDictionary<OrganizationScopedKey, Position> _positions = new();
    private readonly ConcurrentDictionary<OrganizationScopedKey, UserOrganizationMembership> _memberships = new();
    private readonly ConcurrentDictionary<OrganizationScopedKey, UserOrganizationRoleAssignment> _roleAssignments = new();

    public Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateSaveOrganizationUnit(organizationUnit);
        var key = OrganizationScopedKey.FromTenantId(organizationUnit.TenantId, organizationUnit.Id);
        _orgUnits[key] = organizationUnit.Snapshot();
        return Task.CompletedTask;
    }

    public Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidatePointReadId(organizationUnitId, nameof(organizationUnitId));
        var key = OrganizationScopedKey.FromTenantId(tenantId, organizationUnitId);
        if (_orgUnits.TryGetValue(key, out var existing))
            return Task.FromResult<OrganizationUnit?>(existing.Snapshot());
        return Task.FromResult<OrganizationUnit?>(null);
    }

    public Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        IEnumerable<OrganizationUnit> query = _orgUnits.Values;
        if (tenantId is not null)
            query = query.Where(o => o.TenantId == tenantId);

        var result = query.OrderBy(o => o, OrganizationStoreSemantics.OrganizationUnitComparer)
            .Select(o => o.Snapshot())
            .ToList()
            .AsReadOnly();
        return Task.FromResult((IReadOnlyList<OrganizationUnit>)result);
    }

    public Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateSavePosition(position);
        var key = OrganizationScopedKey.FromTenantId(position.TenantId, position.Id);
        _positions[key] = position.Snapshot();
        return Task.CompletedTask;
    }

    public Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidatePointReadId(positionId, nameof(positionId));
        var key = OrganizationScopedKey.FromTenantId(tenantId, positionId);
        if (_positions.TryGetValue(key, out var existing))
            return Task.FromResult<Position?>(existing.Snapshot());
        return Task.FromResult<Position?>(null);
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        IEnumerable<Position> query = _positions.Values;
        if (tenantId is not null)
            query = query.Where(p => p.TenantId == tenantId);

        var result = query.OrderBy(p => p, OrganizationStoreSemantics.PositionComparer)
            .Select(p => p.Snapshot())
            .ToList()
            .AsReadOnly();
        return Task.FromResult((IReadOnlyList<Position>)result);
    }

    public Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateSaveMembership(membership);
        var key = OrganizationScopedKey.FromTenantId(membership.TenantId, membership.Id);
        _memberships[key] = membership.Snapshot();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateUserId(userId, nameof(userId));
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        IEnumerable<UserOrganizationMembership> query = _memberships.Values.Where(m => m.UserId == userId);
        if (tenantId is not null)
            query = query.Where(m => m.TenantId == tenantId);

        var result = query.OrderBy(m => m, OrganizationStoreSemantics.MembershipByUserComparer)
            .Select(m => m.Snapshot())
            .ToList()
            .AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateOrganizationUnitId(organizationUnitId, nameof(organizationUnitId));
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        IEnumerable<UserOrganizationMembership> query = _memberships.Values.Where(m => m.OrganizationUnitId == organizationUnitId);
        if (tenantId is not null)
            query = query.Where(m => m.TenantId == tenantId);

        var result = query.OrderBy(m => m, OrganizationStoreSemantics.MembershipByUnitComparer)
            .Select(m => m.Snapshot())
            .ToList()
            .AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
    }

    public Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateSaveRoleAssignment(assignment);
        var key = OrganizationScopedKey.FromTenantId(assignment.TenantId, assignment.Id);
        _roleAssignments[key] = assignment.Snapshot();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        OrganizationStoreSemantics.ValidateUserId(userId, nameof(userId));
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        IEnumerable<UserOrganizationRoleAssignment> query = _roleAssignments.Values.Where(a => a.UserId == userId);
        if (tenantId is not null)
            query = query.Where(a => a.TenantId == tenantId);

        var result = query.OrderBy(a => a, OrganizationStoreSemantics.RoleAssignmentComparer)
            .Select(a => a.Snapshot())
            .ToList()
            .AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationRoleAssignment>)result);
    }
}
