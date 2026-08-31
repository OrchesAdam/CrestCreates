namespace CrestCreates.Organization.Abstractions;

public readonly record struct OrganizationScopeIdentity
{
    public OrganizationScopeKind Kind { get; }

    public string? TenantId { get; }

    private OrganizationScopeIdentity(OrganizationScopeKind kind, string? tenantId)
    {
        Kind = kind;
        TenantId = tenantId;
    }

    public static OrganizationScopeIdentity Global { get; } = new(OrganizationScopeKind.Global, null);

    public static OrganizationScopeIdentity Tenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId must not be null, empty, or whitespace.", nameof(tenantId));
        return new OrganizationScopeIdentity(OrganizationScopeKind.Tenant, tenantId);
    }

    public void Deconstruct(out OrganizationScopeKind kind, out string? tenantId)
    {
        kind = Kind;
        tenantId = TenantId;
    }
}
