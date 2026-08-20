namespace CrestCreates.Organization.Abstractions;

internal enum OrganizationTenantScopeKind
{
    Global = 0,
    Tenant = 1
}

internal readonly record struct OrganizationScopedKey(
    OrganizationTenantScopeKind ScopeKind,
    string TenantId,
    string Id) : IComparable<OrganizationScopedKey>
{
    public static OrganizationScopedKey FromTenantId(string? tenantId, string id)
    {
        if (tenantId is null)
            return new OrganizationScopedKey(OrganizationTenantScopeKind.Global, "", id);
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Non-null TenantId must not be empty or whitespace.", nameof(tenantId));
        return new OrganizationScopedKey(OrganizationTenantScopeKind.Tenant, tenantId, id);
    }

    public int CompareTo(OrganizationScopedKey other)
    {
        var scopeCompare = ScopeKind.CompareTo(other.ScopeKind);
        if (scopeCompare != 0) return scopeCompare;
        var tenantCompare = string.Compare(TenantId, other.TenantId, StringComparison.Ordinal);
        if (tenantCompare != 0) return tenantCompare;
        return string.Compare(Id, other.Id, StringComparison.Ordinal);
    }
}
