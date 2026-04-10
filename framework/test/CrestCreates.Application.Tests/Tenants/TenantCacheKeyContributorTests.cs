using CrestCreates.Caching;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantCacheKeyContributorTests
{
    private readonly TenantCacheKeyContributor _contributor;

    public TenantCacheKeyContributorTests()
    {
        _contributor = new TenantCacheKeyContributor();
    }

    [Fact]
    public void GetTenantCacheKey_WithTenantId_IncludesTenantPrefix()
    {
        var tenantId = "test-tenant";
        var key = _contributor.GetTenantCacheKey(tenantId, "Data", "123");

        key.Should().Contain("Tenant");
        key.Should().Contain(tenantId);
        key.Should().Contain("Data:123");
    }

    [Fact]
    public void GetTenantCacheKey_WithoutTenantId_OmitsTenantPrefix()
    {
        var key = _contributor.GetTenantCacheKey(null, "Data", "123");

        key.Should().NotContain("Tenant:");
        key.Should().Contain("Data:123");
    }

    [Fact]
    public void GetPermissionCacheKey_ReturnsCorrectFormat()
    {
        var tenantId = "test-tenant";
        var key = _contributor.GetPermissionCacheKey(tenantId, "Role", "Admin");

        key.Should().Contain("Tenant");
        key.Should().Contain(tenantId);
        key.Should().Contain("Permission:Role:Admin");
    }

    [Fact]
    public void GetAuthorizationCacheKey_ReturnsCorrectFormat()
    {
        var tenantId = "test-tenant";
        var key = _contributor.GetAuthorizationCacheKey(tenantId, "Policy", "Read");

        key.Should().Contain("Tenant");
        key.Should().Contain(tenantId);
        key.Should().Contain("Authorization:Policy:Read");
    }
}
