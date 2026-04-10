using CrestCreates.Caching;
using CrestCreates.Domain.Shared.Settings;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Application.Tests.Settings;

public class SettingCacheKeyContributorTests
{
    private readonly SettingCacheKeyContributor _contributor = new();

    [Fact]
    public void GetKeys_ShouldContainScopeDimensions()
    {
        var globalKey = _contributor.GetItemCacheKey(SettingScope.Global, string.Empty, "App.DisplayName");
        var tenantKey = _contributor.GetItemCacheKey(SettingScope.Tenant, "tenant-1", "App.DisplayName", "tenant-1");
        var userKey = _contributor.GetItemCacheKey(SettingScope.User, "user-1", "App.DisplayName", "tenant-1");

        globalKey.Should().Contain("Global");
        tenantKey.Should().Contain("Tenant").And.Contain("tenant-1");
        userKey.Should().Contain("User").And.Contain("user-1").And.Contain("tenant-1");
        globalKey.Should().NotBe(tenantKey);
        tenantKey.Should().NotBe(userKey);
    }
}
