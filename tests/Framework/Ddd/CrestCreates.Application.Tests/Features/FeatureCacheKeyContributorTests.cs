using CrestCreates.Caching;
using CrestCreates.Domain.Shared.Features;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Features;

public class FeatureCacheKeyContributorTests
{
    private readonly FeatureCacheKeyContributor _contributor = new();

    [Fact]
    public void GetItemCacheKey_ShouldContainScopeDimensions()
    {
        var globalKey = _contributor.GetItemCacheKey(FeatureScope.Global, string.Empty, "Identity.UserCreationEnabled");
        var tenantKey = _contributor.GetItemCacheKey(FeatureScope.Tenant, "tenant-1", "Identity.UserCreationEnabled", "tenant-1");

        globalKey.Should().Contain("Global");
        tenantKey.Should().Contain("Tenant").And.Contain("tenant-1");
        globalKey.Should().NotBe(tenantKey);
    }

    [Fact]
    public void GetScopeCacheKey_ShouldContainScopeDimensions()
    {
        var globalKey = _contributor.GetScopeCacheKey(FeatureScope.Global, string.Empty);
        var tenantKey = _contributor.GetScopeCacheKey(FeatureScope.Tenant, "tenant-1", "tenant-1");

        globalKey.Should().Contain("Global");
        tenantKey.Should().Contain("Tenant").And.Contain("tenant-1");
        globalKey.Should().NotBe(tenantKey);
    }

    [Fact]
    public void GetScopeItemPattern_ShouldMatchTheItemKeyShape()
    {
        var itemKey = _contributor.GetItemCacheKey(
            FeatureScope.Tenant, "tenant-1", "Identity.UserCreationEnabled", "tenant-1");
        var pattern = _contributor.GetScopeItemPattern(FeatureScope.Tenant, "tenant-1", "tenant-1");

        itemKey.Should().Be("Feature.Item:Scope:Tenant:tenant-1:tenant-1:Identity.UserCreationEnabled");
        pattern.Should().Be("Feature.Item:Scope:Tenant:tenant-1:tenant-1:*");
        _contributor.GetScopeCacheKey(FeatureScope.Tenant, "tenant-1", "tenant-1")
            .Should().Be("Feature.Scope:Scope:Tenant:tenant-1:tenant-1");
    }

    [Fact]
    public void GetScopeItemPattern_ShouldEscapeLiteralMetacharacters()
    {
        var pattern = _contributor.GetScopeItemPattern(FeatureScope.Tenant, "tenant*1", "tenant*1");

        pattern.Should().Contain(@"tenant\*1");
    }

    [Fact]
    public void DifferentTenants_ShouldHaveDifferentKeys()
    {
        var tenant1Key = _contributor.GetItemCacheKey(FeatureScope.Tenant, "tenant-1", "Identity.UserCreationEnabled", "tenant-1");
        var tenant2Key = _contributor.GetItemCacheKey(FeatureScope.Tenant, "tenant-2", "Identity.UserCreationEnabled", "tenant-2");

        tenant1Key.Should().NotBe(tenant2Key);
    }

    [Fact]
    public void DifferentFeatures_ShouldHaveDifferentKeys()
    {
        var feature1Key = _contributor.GetItemCacheKey(FeatureScope.Global, string.Empty, "Identity.UserCreationEnabled", null);
        var feature2Key = _contributor.GetItemCacheKey(FeatureScope.Global, string.Empty, "Storage.MaxFileCount", null);

        feature1Key.Should().NotBe(feature2Key);
    }
}
