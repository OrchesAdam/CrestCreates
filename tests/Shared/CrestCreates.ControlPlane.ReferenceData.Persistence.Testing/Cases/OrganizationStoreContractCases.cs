using CrestCreates.Organization.Abstractions;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

/// <summary>
/// Runner-free Organization contract cases. Provider projects supply only the
/// Store (and, for the scoped-key case, the domain hierarchy service); the
/// semantic exercise is intentionally shared by InMemory and PostgreSQL.
/// </summary>
public static class OrganizationStoreContractCases
{
    public static async Task RunIdentityAsync(
        IOrganizationStore store,
        OrganizationIdentitySurface surface,
        string prefix)
    {
        switch (surface)
        {
            case OrganizationIdentitySurface.OrganizationUnit:
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit", null));
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit", $"{prefix}-tenant"));
                (await store.GetOrganizationUnitByIdAsync($"{prefix}-unit"))!.TenantId.ShouldBe(null);
                (await store.GetOrganizationUnitByIdAsync($"{prefix}-unit", $"{prefix}-tenant"))!.TenantId.ShouldBe($"{prefix}-tenant");
                break;
            case OrganizationIdentitySurface.Position:
                await store.SavePositionAsync(Position($"{prefix}-position", null));
                await store.SavePositionAsync(Position($"{prefix}-position", $"{prefix}-tenant"));
                (await store.GetPositionByIdAsync($"{prefix}-position"))!.TenantId.ShouldBe(null);
                (await store.GetPositionByIdAsync($"{prefix}-position", $"{prefix}-tenant"))!.TenantId.ShouldBe($"{prefix}-tenant");
                break;
            case OrganizationIdentitySurface.Membership:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership", $"{prefix}-user", null));
                await store.SaveMembershipAsync(Membership($"{prefix}-membership", $"{prefix}-user", $"{prefix}-tenant"));
                (await store.GetMembershipsByUserAsync($"{prefix}-user")).Single(x => x.TenantId is null).TenantId.ShouldBe(null);
                (await store.GetMembershipsByUserAsync($"{prefix}-user", $"{prefix}-tenant"))!.Single().TenantId.ShouldBe($"{prefix}-tenant");
                break;
            case OrganizationIdentitySurface.RoleAssignment:
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role", $"{prefix}-user", null));
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role", $"{prefix}-user", $"{prefix}-tenant"));
                (await store.GetRoleAssignmentsByUserAsync($"{prefix}-user")).Single(x => x.TenantId is null).TenantId.ShouldBe(null);
                (await store.GetRoleAssignmentsByUserAsync($"{prefix}-user", $"{prefix}-tenant"))!.Single().TenantId.ShouldBe($"{prefix}-tenant");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, null);
        }
    }

    public static async Task RunExplicitQueryAsync(
        IOrganizationStore store,
        OrganizationQuerySurface surface,
        string prefix)
    {
        switch (surface)
        {
            case OrganizationQuerySurface.Units:
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit-a", $"{prefix}-a"));
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit-b", $"{prefix}-b"));
                (await store.GetOrganizationUnitsAsync($"{prefix}-a")).Select(x => x.Id).ShouldEqual($"{prefix}-unit-a");
                break;
            case OrganizationQuerySurface.Positions:
                await store.SavePositionAsync(Position($"{prefix}-position-a", $"{prefix}-a"));
                await store.SavePositionAsync(Position($"{prefix}-position-b", $"{prefix}-b"));
                (await store.GetPositionsAsync($"{prefix}-a")).Select(x => x.Id).ShouldEqual($"{prefix}-position-a");
                break;
            case OrganizationQuerySurface.MembershipsByUser:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-a", $"{prefix}-user", $"{prefix}-a"));
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-b", $"{prefix}-user", $"{prefix}-b"));
                (await store.GetMembershipsByUserAsync($"{prefix}-user", $"{prefix}-a")).Select(x => x.Id).ShouldEqual($"{prefix}-membership-a");
                break;
            case OrganizationQuerySurface.MembershipsByUnit:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-a", $"{prefix}-user-a", $"{prefix}-a", $"{prefix}-unit"));
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-b", $"{prefix}-user-b", $"{prefix}-b", $"{prefix}-unit"));
                (await store.GetMembershipsByOrganizationUnitAsync($"{prefix}-unit", $"{prefix}-a")).Select(x => x.Id).ShouldEqual($"{prefix}-membership-a");
                break;
            case OrganizationQuerySurface.RolesByUser:
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role-a", $"{prefix}-user", $"{prefix}-a"));
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role-b", $"{prefix}-user", $"{prefix}-b"));
                (await store.GetRoleAssignmentsByUserAsync($"{prefix}-user", $"{prefix}-a")).Select(x => x.Id).ShouldEqual($"{prefix}-role-a");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, null);
        }
    }

    public static async Task RunUnfilteredQueryAsync(
        IOrganizationStore store,
        OrganizationQuerySurface surface,
        string prefix)
    {
        switch (surface)
        {
            case OrganizationQuerySurface.Units:
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit-global", null));
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit-tenant", $"{prefix}-tenant"));
                (await store.GetOrganizationUnitsAsync()).Count(x => x.Id.StartsWith(prefix, StringComparison.Ordinal)).ShouldBe(2);
                break;
            case OrganizationQuerySurface.Positions:
                await store.SavePositionAsync(Position($"{prefix}-position-global", null));
                await store.SavePositionAsync(Position($"{prefix}-position-tenant", $"{prefix}-tenant"));
                (await store.GetPositionsAsync()).Count(x => x.Id.StartsWith(prefix, StringComparison.Ordinal)).ShouldBe(2);
                break;
            case OrganizationQuerySurface.MembershipsByUser:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-global", $"{prefix}-user", null));
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-tenant", $"{prefix}-user", $"{prefix}-tenant"));
                (await store.GetMembershipsByUserAsync($"{prefix}-user")).Count.ShouldBe(2);
                break;
            case OrganizationQuerySurface.MembershipsByUnit:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-global", $"{prefix}-user-global", null, $"{prefix}-unit"));
                await store.SaveMembershipAsync(Membership($"{prefix}-membership-tenant", $"{prefix}-user-tenant", $"{prefix}-tenant", $"{prefix}-unit"));
                (await store.GetMembershipsByOrganizationUnitAsync($"{prefix}-unit")).Count.ShouldBe(2);
                break;
            case OrganizationQuerySurface.RolesByUser:
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role-global", $"{prefix}-user", null));
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role-tenant", $"{prefix}-user", $"{prefix}-tenant"));
                (await store.GetRoleAssignmentsByUserAsync($"{prefix}-user")).Count.ShouldBe(2);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, null);
        }
    }

    public static async Task RunEntitySnapshotAsync(
        IOrganizationStore store,
        OrganizationEntitySurface surface,
        string prefix)
    {
        var tenant = $"{prefix}-tenant";
        switch (surface)
        {
            case OrganizationEntitySurface.OrganizationUnit:
                var unit = Unit($"{prefix}-unit", tenant);
                await store.SaveOrganizationUnitAsync(unit);
                (await store.GetOrganizationUnitByIdAsync(unit.Id, tenant))!.Name.ShouldBe(unit.Name);
                break;
            case OrganizationEntitySurface.Position:
                var position = Position($"{prefix}-position", tenant);
                await store.SavePositionAsync(position);
                (await store.GetPositionByIdAsync(position.Id, tenant))!.Name.ShouldBe(position.Name);
                break;
            case OrganizationEntitySurface.Membership:
                var membership = Membership($"{prefix}-membership", $"{prefix}-user", tenant);
                await store.SaveMembershipAsync(membership);
                (await store.GetMembershipsByUserAsync(membership.UserId, tenant)).Single().Id.ShouldBe(membership.Id);
                break;
            case OrganizationEntitySurface.RoleAssignment:
                var role = Role($"{prefix}-role", $"{prefix}-user", tenant);
                await store.SaveRoleAssignmentAsync(role);
                (await store.GetRoleAssignmentsByUserAsync(role.UserId, tenant)).Single().Id.ShouldBe(role.Id);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, null);
        }
    }

    public static async Task RunDetachedReadAsync(
        IOrganizationStore store,
        OrganizationReadSurface surface,
        string prefix)
    {
        const string tenant = "tenant";
        switch (surface)
        {
            case OrganizationReadSurface.UnitById:
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit", tenant));
                var unit1 = await store.GetOrganizationUnitByIdAsync($"{prefix}-unit", tenant);
                var unit2 = await store.GetOrganizationUnitByIdAsync($"{prefix}-unit", tenant);
                (unit1 is not null && unit2 is not null && !ReferenceEquals(unit1, unit2)).ShouldBe(true);
                break;
            case OrganizationReadSurface.Units:
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit", tenant));
                var units1 = await store.GetOrganizationUnitsAsync(tenant);
                var units2 = await store.GetOrganizationUnitsAsync(tenant);
                (units1[0] is not null && units2[0] is not null && !ReferenceEquals(units1[0], units2[0])).ShouldBe(true);
                break;
            case OrganizationReadSurface.PositionById:
                await store.SavePositionAsync(Position($"{prefix}-position", tenant));
                var position1 = await store.GetPositionByIdAsync($"{prefix}-position", tenant);
                var position2 = await store.GetPositionByIdAsync($"{prefix}-position", tenant);
                (position1 is not null && position2 is not null && !ReferenceEquals(position1, position2)).ShouldBe(true);
                break;
            case OrganizationReadSurface.Positions:
                await store.SavePositionAsync(Position($"{prefix}-position", tenant));
                var positions1 = await store.GetPositionsAsync(tenant);
                var positions2 = await store.GetPositionsAsync(tenant);
                (positions1[0] is not null && positions2[0] is not null && !ReferenceEquals(positions1[0], positions2[0])).ShouldBe(true);
                break;
            case OrganizationReadSurface.MembershipsByUser:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership", $"{prefix}-user", tenant));
                var membershipsByUser1 = await store.GetMembershipsByUserAsync($"{prefix}-user", tenant);
                var membershipsByUser2 = await store.GetMembershipsByUserAsync($"{prefix}-user", tenant);
                (!ReferenceEquals(membershipsByUser1[0], membershipsByUser2[0])).ShouldBe(true);
                break;
            case OrganizationReadSurface.MembershipsByUnit:
                await store.SaveMembershipAsync(Membership($"{prefix}-membership", $"{prefix}-user", tenant, $"{prefix}-unit"));
                var membershipsByUnit1 = await store.GetMembershipsByOrganizationUnitAsync($"{prefix}-unit", tenant);
                var membershipsByUnit2 = await store.GetMembershipsByOrganizationUnitAsync($"{prefix}-unit", tenant);
                (!ReferenceEquals(membershipsByUnit1[0], membershipsByUnit2[0])).ShouldBe(true);
                break;
            case OrganizationReadSurface.RolesByUser:
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-role", $"{prefix}-user", tenant));
                var roles1 = await store.GetRoleAssignmentsByUserAsync($"{prefix}-user", tenant);
                var roles2 = await store.GetRoleAssignmentsByUserAsync($"{prefix}-user", tenant);
                (!ReferenceEquals(roles1[0], roles2[0])).ShouldBe(true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(surface), surface, null);
        }
    }

    public static async Task RunCreatedAtAsync(
        IOrganizationStore store,
        OrganizationCreatedAtVariant variant,
        string prefix)
    {
        var first = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(5));
        switch (variant)
        {
            case OrganizationCreatedAtVariant.UnitNonZeroOffset:
                await store.SaveOrganizationUnitAsync(Unit($"{prefix}-unit", $"{prefix}-tenant", createdAt: first));
                (await store.GetOrganizationUnitByIdAsync($"{prefix}-unit", $"{prefix}-tenant"))!.CreatedAt.ShouldBe(first);
                break;
            case OrganizationCreatedAtVariant.PositionNonZeroOffset:
                await store.SavePositionAsync(Position($"{prefix}-position", $"{prefix}-tenant", first));
                (await store.GetPositionByIdAsync($"{prefix}-position", $"{prefix}-tenant"))!.CreatedAt.ShouldBe(first);
                break;
            case OrganizationCreatedAtVariant.MembershipNonZeroOffset:
            case OrganizationCreatedAtVariant.MembershipHundredNanosecondOrder:
                await store.SaveMembershipAsync(Membership($"{prefix}-first", $"{prefix}-user", $"{prefix}-tenant", createdAt: first));
                await store.SaveMembershipAsync(Membership($"{prefix}-second", $"{prefix}-user", $"{prefix}-tenant", createdAt: first.AddTicks(1)));
                (await store.GetMembershipsByUserAsync($"{prefix}-user", $"{prefix}-tenant")).Select(x => x.Id).ShouldEqual($"{prefix}-first", $"{prefix}-second");
                break;
            case OrganizationCreatedAtVariant.RoleAssignmentNonZeroOffset:
            case OrganizationCreatedAtVariant.RoleAssignmentHundredNanosecondOrder:
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-first", $"{prefix}-user", $"{prefix}-tenant", first));
                await store.SaveRoleAssignmentAsync(Role($"{prefix}-second", $"{prefix}-user", $"{prefix}-tenant", first.AddTicks(1)));
                (await store.GetRoleAssignmentsByUserAsync($"{prefix}-user", $"{prefix}-tenant")).Select(x => x.Id).ShouldEqual($"{prefix}-first", $"{prefix}-second");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(variant), variant, null);
        }
    }

    public static async Task RunScopedKeyAsync(
        IOrganizationStore store,
        IOrganizationHierarchyService hierarchy,
        string prefix)
    {
        await store.SaveOrganizationUnitAsync(Unit($"{prefix}-c", "a:b"));
        await store.SaveOrganizationUnitAsync(Unit($"{prefix}-b:c", "a"));
        await store.SaveOrganizationUnitAsync(Unit($"{prefix}-child-ab", "a:b", $"{prefix}-c"));
        await store.SaveOrganizationUnitAsync(Unit($"{prefix}-child-a", "a", $"{prefix}-b:c"));
        (await hierarchy.GetDescendantsAsync($"{prefix}-c", "a:b")).Select(x => x.Id).ShouldEqual($"{prefix}-child-ab");
        (await hierarchy.GetDescendantsAsync($"{prefix}-b:c", "a")).Select(x => x.Id).ShouldEqual($"{prefix}-child-a");
    }

    private static OrganizationUnit Unit(string id, string? tenantId, string? parentId = null, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, Name = id, ParentId = parentId, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static Position Position(string id, string? tenantId, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, Name = id, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static UserOrganizationMembership Membership(string id, string userId, string? tenantId, string unitId = "unit", DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, UserId = userId, OrganizationUnitId = unitId, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static UserOrganizationRoleAssignment Role(string id, string userId, string? tenantId, DateTimeOffset? createdAt = null)
        => new() { Id = id, TenantId = tenantId, UserId = userId, RoleId = id, CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch };

    private static void ShouldBe(this object? actual, object? expected)
    {
        if (!Equals(actual, expected))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void ShouldEqual<T>(this IEnumerable<T> actual, params T[] expected)
    {
        var values = actual.ToArray();
        if (!values.SequenceEqual(expected))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", values)}].");
    }
}
