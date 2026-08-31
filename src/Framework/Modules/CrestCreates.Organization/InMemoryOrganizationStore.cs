using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly ConcurrentDictionary<string, OrganizationScopeGuard> _scopes = new();

    private OrganizationScopeGuard GetOrCreateScope(string tenantId)
        => _scopes.GetOrAdd(tenantId, static _ => new OrganizationScopeGuard());

    public Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveOrganizationUnit(organizationUnit);
        var tenantId = organizationUnit.TenantId ?? string.Empty;
        var scope = GetOrCreateScope(tenantId);
        scope.Acquire();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked
            {
                var current = scope.Value;
                scope.Value = current with
                {
                    Generation = current.Generation + 1,
                    OrganizationUnits = current.OrganizationUnits.SetItem(
                        OrganizationScopedKey.FromTenantId(organizationUnit.TenantId, organizationUnit.Id),
                        organizationUnit.Snapshot())
                };
            }
        }
        finally
        {
            scope.Release();
        }
        return Task.CompletedTask;
    }

    public Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidatePointReadId(organizationUnitId, nameof(organizationUnitId));
        if (tenantId is null)
        {
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                if (state.OrganizationUnits.TryGetValue(new OrganizationScopedKey(OrganizationTenantScopeKind.Global, "", organizationUnitId), out var existing))
                    return Task.FromResult<OrganizationUnit?>(existing.Snapshot());
            }
            return Task.FromResult<OrganizationUnit?>(null);
        }
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            if (state.OrganizationUnits.TryGetValue(OrganizationScopedKey.FromTenantId(tenantId, organizationUnitId), out var existing))
                return Task.FromResult<OrganizationUnit?>(existing.Snapshot());
        }
        return Task.FromResult<OrganizationUnit?>(null);
    }

    public Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (tenantId is null)
        {
            var allUnits = new List<OrganizationUnit>();
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                foreach (var unit in state.OrganizationUnits.Values)
                    allUnits.Add(unit.Snapshot());
            }
            allUnits.Sort(OrganizationStoreSemantics.OrganizationUnitComparer);
            return Task.FromResult((IReadOnlyList<OrganizationUnit>)allUnits.AsReadOnly());
        }
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            var result = state.OrganizationUnits.Values
                .Where(o => o.TenantId == tenantId)
                .Select(o => o.Snapshot())
                .OrderBy(o => o, OrganizationStoreSemantics.OrganizationUnitComparer)
                .ToList()
                .AsReadOnly();
            return Task.FromResult((IReadOnlyList<OrganizationUnit>)result);
        }
        return Task.FromResult((IReadOnlyList<OrganizationUnit>)Array.Empty<OrganizationUnit>());
    }

    public Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSavePosition(position);
        var tenantId = position.TenantId ?? string.Empty;
        var scope = GetOrCreateScope(tenantId);
        scope.Acquire();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked
            {
                var current = scope.Value;
                scope.Value = current with
                {
                    Generation = current.Generation + 1,
                    Positions = current.Positions.SetItem(
                        OrganizationScopedKey.FromTenantId(position.TenantId, position.Id),
                        position.Snapshot())
                };
            }
        }
        finally
        {
            scope.Release();
        }
        return Task.CompletedTask;
    }

    public Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidatePointReadId(positionId, nameof(positionId));
        if (tenantId is null)
        {
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                if (state.Positions.TryGetValue(new OrganizationScopedKey(OrganizationTenantScopeKind.Global, "", positionId), out var existing))
                    return Task.FromResult<Position?>(existing.Snapshot());
            }
            return Task.FromResult<Position?>(null);
        }
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            if (state.Positions.TryGetValue(OrganizationScopedKey.FromTenantId(tenantId, positionId), out var existing))
                return Task.FromResult<Position?>(existing.Snapshot());
        }
        return Task.FromResult<Position?>(null);
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (tenantId is null)
        {
            var allPositions = new List<Position>();
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                foreach (var position in state.Positions.Values)
                    allPositions.Add(position.Snapshot());
            }
            allPositions.Sort(OrganizationStoreSemantics.PositionComparer);
            return Task.FromResult((IReadOnlyList<Position>)allPositions.AsReadOnly());
        }
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            var result = state.Positions.Values
                .Where(p => p.TenantId == tenantId)
                .Select(p => p.Snapshot())
                .OrderBy(p => p, OrganizationStoreSemantics.PositionComparer)
                .ToList()
                .AsReadOnly();
            return Task.FromResult((IReadOnlyList<Position>)result);
        }
        return Task.FromResult((IReadOnlyList<Position>)Array.Empty<Position>());
    }

    public Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveMembership(membership);
        var tenantId = membership.TenantId ?? string.Empty;
        var scope = GetOrCreateScope(tenantId);
        scope.Acquire();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked
            {
                var current = scope.Value;
                scope.Value = current with
                {
                    Generation = current.Generation + 1,
                    Memberships = current.Memberships.SetItem(
                        OrganizationScopedKey.FromTenantId(membership.TenantId, membership.Id),
                        membership.Snapshot())
                };
            }
        }
        finally
        {
            scope.Release();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateUserId(userId, nameof(userId));
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (tenantId is null)
        {
            var allMemberships = new List<UserOrganizationMembership>();
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                foreach (var membership in state.Memberships.Values.Where(m => m.UserId == userId))
                    allMemberships.Add(membership.Snapshot());
            }
            allMemberships.Sort(OrganizationStoreSemantics.MembershipByUserComparer);
            return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)allMemberships.AsReadOnly());
        }
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            var result = state.Memberships.Values
                .Where(m => m.UserId == userId && m.TenantId == tenantId)
                .Select(m => m.Snapshot())
                .OrderBy(m => m, OrganizationStoreSemantics.MembershipByUserComparer)
                .ToList()
                .AsReadOnly();
            return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
        }
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)Array.Empty<UserOrganizationMembership>());
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateOrganizationUnitId(organizationUnitId, nameof(organizationUnitId));
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (tenantId is null)
        {
            var allMemberships = new List<UserOrganizationMembership>();
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                foreach (var membership in state.Memberships.Values.Where(m => m.OrganizationUnitId == organizationUnitId))
                    allMemberships.Add(membership.Snapshot());
            }
            allMemberships.Sort(OrganizationStoreSemantics.MembershipByUnitComparer);
            return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)allMemberships.AsReadOnly());
        }
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            var result = state.Memberships.Values
                .Where(m => m.OrganizationUnitId == organizationUnitId && m.TenantId == tenantId)
                .Select(m => m.Snapshot())
                .OrderBy(m => m, OrganizationStoreSemantics.MembershipByUnitComparer)
                .ToList()
                .AsReadOnly();
            return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
        }
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)Array.Empty<UserOrganizationMembership>());
    }

    public Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateSaveRoleAssignment(assignment);
        var tenantId = assignment.TenantId ?? string.Empty;
        var scope = GetOrCreateScope(tenantId);
        scope.Acquire();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked
            {
                var current = scope.Value;
                scope.Value = current with
                {
                    Generation = current.Generation + 1,
                    RoleAssignments = current.RoleAssignments.SetItem(
                        OrganizationScopedKey.FromTenantId(assignment.TenantId, assignment.Id),
                        assignment.Snapshot())
                };
            }
        }
        finally
        {
            scope.Release();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateUserId(userId, nameof(userId));
        OrganizationStoreSemantics.ValidateQueryTenantId(tenantId);
        if (tenantId is null)
        {
            var allRoles = new List<UserOrganizationRoleAssignment>();
            foreach (var kvp in _scopes)
            {
                var state = kvp.Value.Value;
                foreach (var role in state.RoleAssignments.Values.Where(a => a.UserId == userId))
                    allRoles.Add(role.Snapshot());
            }
            allRoles.Sort(OrganizationStoreSemantics.RoleAssignmentComparer);
            return Task.FromResult((IReadOnlyList<UserOrganizationRoleAssignment>)allRoles.AsReadOnly());
        }
        if (_scopes.TryGetValue(tenantId, out var scope))
        {
            var state = scope.Value;
            var result = state.RoleAssignments.Values
                .Where(a => a.UserId == userId && a.TenantId == tenantId)
                .Select(a => a.Snapshot())
                .OrderBy(a => a, OrganizationStoreSemantics.RoleAssignmentComparer)
                .ToList()
                .AsReadOnly();
            return Task.FromResult((IReadOnlyList<UserOrganizationRoleAssignment>)result);
        }
        return Task.FromResult((IReadOnlyList<UserOrganizationRoleAssignment>)Array.Empty<UserOrganizationRoleAssignment>());
    }

    public Task<OrganizationScopeGenerationRead> ReadScopeGenerationAsync(OrganizationScopeIdentity scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OrganizationStoreSemantics.ValidateScopeIdentity(scope);
        var tenantId = OrganizationStoreSemantics.NormalizeTenantId(scope);
        if (_scopes.TryGetValue(tenantId, out var guard))
            return Task.FromResult(OrganizationScopeGenerationRead.Available(guard.Value.Generation));
        return Task.FromResult(OrganizationScopeGenerationRead.Available(0));
    }
}
