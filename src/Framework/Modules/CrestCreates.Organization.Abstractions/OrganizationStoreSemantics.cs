namespace CrestCreates.Organization.Abstractions;

internal static class OrganizationStoreSemantics
{
    public static void ValidateSaveOrganizationUnit(OrganizationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (string.IsNullOrEmpty(unit.Id))
            throw new ArgumentException("OrganizationUnit.Id must not be null or empty.", nameof(unit));
        if (unit.TenantId is not null && string.IsNullOrWhiteSpace(unit.TenantId))
            throw new ArgumentException("Non-null OrganizationUnit.TenantId must not be empty or whitespace.", nameof(unit));
    }

    public static void ValidateSavePosition(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (string.IsNullOrEmpty(position.Id))
            throw new ArgumentException("Position.Id must not be null or empty.", nameof(position));
        if (position.TenantId is not null && string.IsNullOrWhiteSpace(position.TenantId))
            throw new ArgumentException("Non-null Position.TenantId must not be empty or whitespace.", nameof(position));
    }

    public static void ValidateSaveMembership(UserOrganizationMembership membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        if (string.IsNullOrEmpty(membership.Id))
            throw new ArgumentException("Membership.Id must not be null or empty.", nameof(membership));
        if (membership.TenantId is not null && string.IsNullOrWhiteSpace(membership.TenantId))
            throw new ArgumentException("Non-null Membership.TenantId must not be empty or whitespace.", nameof(membership));
        if (string.IsNullOrEmpty(membership.UserId))
            throw new ArgumentException("Membership.UserId must not be null or empty.", nameof(membership));
        if (string.IsNullOrEmpty(membership.OrganizationUnitId))
            throw new ArgumentException("Membership.OrganizationUnitId must not be null or empty.", nameof(membership));
        if (membership.PositionId is not null && string.IsNullOrEmpty(membership.PositionId))
            throw new ArgumentException("Non-null Membership.PositionId must not be empty.", nameof(membership));
    }

    public static void ValidateSaveRoleAssignment(UserOrganizationRoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (string.IsNullOrEmpty(assignment.Id))
            throw new ArgumentException("RoleAssignment.Id must not be null or empty.", nameof(assignment));
        if (assignment.TenantId is not null && string.IsNullOrWhiteSpace(assignment.TenantId))
            throw new ArgumentException("Non-null RoleAssignment.TenantId must not be empty or whitespace.", nameof(assignment));
        if (string.IsNullOrEmpty(assignment.UserId))
            throw new ArgumentException("RoleAssignment.UserId must not be null or empty.", nameof(assignment));
        if (string.IsNullOrEmpty(assignment.RoleId))
            throw new ArgumentException("RoleAssignment.RoleId must not be null or empty.", nameof(assignment));
        if (assignment.OrganizationUnitId is not null && string.IsNullOrEmpty(assignment.OrganizationUnitId))
            throw new ArgumentException("Non-null RoleAssignment.OrganizationUnitId must not be empty.", nameof(assignment));
    }

    public static void ValidatePointReadId(string id, string paramName)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("Id must not be null or empty.", paramName);
    }

    public static void ValidateUserId(string userId, string paramName)
    {
        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("UserId must not be null or empty.", paramName);
    }

    public static void ValidateOrganizationUnitId(string organizationUnitId, string paramName)
    {
        if (string.IsNullOrEmpty(organizationUnitId))
            throw new ArgumentException("OrganizationUnitId must not be null or empty.", paramName);
    }

    public static void ValidateQueryTenantId(string? tenantId)
    {
        if (tenantId is not null && string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Non-null tenantId must not be empty or whitespace.", nameof(tenantId));
    }

    public static void ValidateScopeIdentity(OrganizationScopeIdentity scope)
    {
        if (scope.Kind == OrganizationScopeKind.Unknown)
            throw new ArgumentException("Scope identity must be Global or Tenant; Unknown is not a valid generation scope.", nameof(scope));
        if (scope.Kind == OrganizationScopeKind.Global && scope.TenantId is not null)
            throw new ArgumentException("Global scope must have a null TenantId.", nameof(scope));
        if (scope.Kind == OrganizationScopeKind.Tenant && string.IsNullOrWhiteSpace(scope.TenantId))
            throw new ArgumentException("Tenant scope must have a non-blank TenantId.", nameof(scope));
    }

    public static string NormalizeScopeKind(OrganizationScopeIdentity scope)
        => scope.Kind switch
        {
            OrganizationScopeKind.Global => "global",
            OrganizationScopeKind.Tenant => "tenant",
            _ => throw new ArgumentException($"Unknown scope kind: {scope.Kind}", nameof(scope))
        };

    public static string NormalizeTenantId(OrganizationScopeIdentity scope)
        => scope.Kind switch
        {
            OrganizationScopeKind.Global => string.Empty,
            OrganizationScopeKind.Tenant => scope.TenantId!,
            _ => throw new ArgumentException($"Unknown scope kind: {scope.Kind}", nameof(scope))
        };

    public static IComparer<OrganizationUnit> OrganizationUnitComparer { get; } = new UnitOrderComparer();
    public static IComparer<Position> PositionComparer { get; } = new PositionOrderComparer();
    public static IComparer<UserOrganizationMembership> MembershipByUserComparer { get; } = new MembershipOrderComparer();
    public static IComparer<UserOrganizationMembership> MembershipByUnitComparer { get; } = new MembershipOrderComparer();
    public static IComparer<UserOrganizationRoleAssignment> RoleAssignmentComparer { get; } = new RoleAssignmentOrderComparer();

    private sealed class UnitOrderComparer : IComparer<OrganizationUnit>
    {
        public int Compare(OrganizationUnit? x, OrganizationUnit? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var sortCompare = x.SortOrder.CompareTo(y.SortOrder);
            if (sortCompare != 0) return sortCompare;

            var xKey = OrganizationScopedKey.FromTenantId(x.TenantId, x.Id);
            var yKey = OrganizationScopedKey.FromTenantId(y.TenantId, y.Id);
            return xKey.CompareTo(yKey);
        }
    }

    private sealed class PositionOrderComparer : IComparer<Position>
    {
        public int Compare(Position? x, Position? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var xKey = OrganizationScopedKey.FromTenantId(x.TenantId, x.Id);
            var yKey = OrganizationScopedKey.FromTenantId(y.TenantId, y.Id);
            return xKey.CompareTo(yKey);
        }
    }

    private sealed class MembershipOrderComparer : IComparer<UserOrganizationMembership>
    {
        public int Compare(UserOrganizationMembership? x, UserOrganizationMembership? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var ticksCompare = x.CreatedAt.UtcTicks.CompareTo(y.CreatedAt.UtcTicks);
            if (ticksCompare != 0) return ticksCompare;

            var xKey = OrganizationScopedKey.FromTenantId(x.TenantId, x.Id);
            var yKey = OrganizationScopedKey.FromTenantId(y.TenantId, y.Id);
            return xKey.CompareTo(yKey);
        }
    }

    private sealed class RoleAssignmentOrderComparer : IComparer<UserOrganizationRoleAssignment>
    {
        public int Compare(UserOrganizationRoleAssignment? x, UserOrganizationRoleAssignment? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var ticksCompare = x.CreatedAt.UtcTicks.CompareTo(y.CreatedAt.UtcTicks);
            if (ticksCompare != 0) return ticksCompare;

            var xKey = OrganizationScopedKey.FromTenantId(x.TenantId, x.Id);
            var yKey = OrganizationScopedKey.FromTenantId(y.TenantId, y.Id);
            return xKey.CompareTo(yKey);
        }
    }
}
