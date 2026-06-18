using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly ConcurrentDictionary<string, OrganizationUnit> _orgUnits = new();
    private readonly ConcurrentDictionary<string, Position> _positions = new();
    private readonly ConcurrentDictionary<string, UserOrganizationMembership> _memberships = new();
    private readonly ConcurrentDictionary<string, UserOrganizationRoleAssignment> _roleAssignments = new();

    // ── OrganizationUnit (composite key: tenantId + ":" + id) ──

    public Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default)
    {
        var key = CompKey(organizationUnit.TenantId, organizationUnit.Id);
        _orgUnits[key] = organizationUnit.Clone();
        return Task.CompletedTask;
    }

    public Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var key = CompKey(tenantId, organizationUnitId);
        if (_orgUnits.TryGetValue(key, out var existing))
            return Task.FromResult<OrganizationUnit?>(existing.Clone());
        return Task.FromResult<OrganizationUnit?>(null);
    }

    public Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<OrganizationUnit> query = _orgUnits.Values;
        if (tenantId is not null)
            query = query.Where(o => o.TenantId == tenantId);

        var result = query.Select(o => o.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<OrganizationUnit>)result);
    }

    // ── Position (composite key: tenantId + ":" + id) ──

    public Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        var key = CompKey(position.TenantId, position.Id);
        _positions[key] = position.Clone();
        return Task.CompletedTask;
    }

    public Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var key = CompKey(tenantId, positionId);
        if (_positions.TryGetValue(key, out var existing))
            return Task.FromResult<Position?>(existing.Clone());
        return Task.FromResult<Position?>(null);
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<Position> query = _positions.Values;
        if (tenantId is not null)
            query = query.Where(p => p.TenantId == tenantId);

        var result = query.Select(p => p.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<Position>)result);
    }

    // ── Helpers ──

    private static string CompKey(string? tenantId, string id) => $"{tenantId ?? ""}:{id}";

    // ── UserOrganizationMembership (composite key: tenantId + ":" + id) ──

    public Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        var key = CompKey(membership.TenantId, membership.Id);
        _memberships[key] = membership.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<UserOrganizationMembership> query = _memberships.Values.Where(m => m.UserId == userId);
        if (tenantId is not null)
            query = query.Where(m => m.TenantId == tenantId);

        var result = query.Select(m => m.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<UserOrganizationMembership> query = _memberships.Values.Where(m => m.OrganizationUnitId == organizationUnitId);
        if (tenantId is not null)
            query = query.Where(m => m.TenantId == tenantId);

        var result = query.Select(m => m.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
    }

    // ── UserOrganizationRoleAssignment (composite key: tenantId + ":" + id) ──

    public Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        var key = CompKey(assignment.TenantId, assignment.Id);
        _roleAssignments[key] = assignment.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<UserOrganizationRoleAssignment> query = _roleAssignments.Values.Where(a => a.UserId == userId);
        if (tenantId is not null)
            query = query.Where(a => a.TenantId == tenantId);

        var result = query.Select(a => a.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationRoleAssignment>)result);
    }
}
