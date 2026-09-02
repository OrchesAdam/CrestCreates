using System.Collections.Concurrent;
using System.Collections.Immutable;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

internal sealed record OrganizationScopeState(
    long Generation,
    ImmutableDictionary<OrganizationScopedKey, OrganizationUnit> OrganizationUnits,
    ImmutableDictionary<OrganizationScopedKey, Position> Positions,
    ImmutableDictionary<OrganizationScopedKey, UserOrganizationMembership> Memberships,
    ImmutableDictionary<OrganizationScopedKey, UserOrganizationRoleAssignment> RoleAssignments)
{
    public static OrganizationScopeState Empty { get; } = new(
        0,
        ImmutableDictionary<OrganizationScopedKey, OrganizationUnit>.Empty,
        ImmutableDictionary<OrganizationScopedKey, Position>.Empty,
        ImmutableDictionary<OrganizationScopedKey, UserOrganizationMembership>.Empty,
        ImmutableDictionary<OrganizationScopedKey, UserOrganizationRoleAssignment>.Empty);
}

internal sealed class OrganizationScopeGuard
{
    private readonly object _lock = new();
    private OrganizationScopeState _value = OrganizationScopeState.Empty;

    public OrganizationScopeState Value
    {
        get
        {
            lock (_lock) return _value;
        }
        set
        {
            lock (_lock) _value = value;
        }
    }

    public void Acquire() => Monitor.Enter(_lock);
    public void Release() => Monitor.Exit(_lock);
}
