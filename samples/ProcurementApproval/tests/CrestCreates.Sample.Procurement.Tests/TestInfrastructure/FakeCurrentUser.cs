using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

public sealed class FakeCurrentUser : ICurrentUser
{
    public string Id { get; set; } = "user-1";
    public string UserName { get; set; } = "testuser";
    public bool IsAuthenticated { get; set; } = true;
    public string TenantId { get; set; } = "tenant-1";
    public string[] Roles { get; set; } = [];
    public Guid? OrganizationId { get; set; }
    public IReadOnlyList<Guid> OrganizationIds { get; set; } = [];
    public int DataScopeValue { get; set; }
    public bool IsSuperAdmin { get; set; }

    public string FindClaimValue(string claimType) => string.Empty;
    public string[] FindClaimValues(string claimType) => [];
    public bool IsInRole(string roleName) => Roles.Contains(roleName);
    public bool IsInOrganization(Guid orgId) => OrganizationIds.Contains(orgId);
}
