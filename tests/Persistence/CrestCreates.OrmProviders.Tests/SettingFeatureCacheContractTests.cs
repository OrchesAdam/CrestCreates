using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Application.Settings;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.Domain.Shared.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.OrmProviders.Tests;

public class SettingFeatureCacheContractTests
{
    private const string SettingName = "Sample.Global";
    private const string FeatureName = "Sample.Feature";

    [Fact]
    public async Task Setting_scope_invalidation_refreshes_items_and_list_and_preserves_sibling_partitions()
    {
        var cache = CreateCache();
        var service = new CrestCacheService(cache, new CrestCacheKeyGenerator());
        var contributor = new SettingCacheKeyContributor();
        var invalidator = new SettingCacheInvalidator(service, contributor);

        var globalRow = new SettingValue(Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "old", false, null);
        var tenantRow = new SettingValue(Guid.NewGuid(), SettingName, SettingScope.User, "user-1", "user-old", false, "tenant-a");
        var globalFindCalls = 0;
        var globalListCalls = 0;
        var userFindCalls = 0;

        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(SettingName, SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                globalFindCalls++;
                return globalRow;
            });
        repository
            .Setup(repo => repo.FindAsync(SettingName, SettingScope.User, "user-1", "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                userFindCalls++;
                return tenantRow;
            });
        repository
            .Setup(repo => repo.GetListByScopeAsync(SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                globalListCalls++;
                return new List<SettingValue> { globalRow };
            });

        var store = new SettingStore(repository.Object, Mock.Of<ISettingEncryptionService>(), service, contributor);

        (await store.GetOrNullAsync(SettingName, SettingScope.Global, string.Empty))!.Value.Should().Be("old");
        (await store.GetOrNullAsync(SettingName, SettingScope.Global, string.Empty))!.Value.Should().Be("old");
        globalFindCalls.Should().Be(1, "the second read must come from the item cache");

        (await store.GetListAsync(SettingScope.Global, string.Empty)).Single().Value.Should().Be("old");
        (await store.GetListAsync(SettingScope.Global, string.Empty)).Single().Value.Should().Be("old");
        globalListCalls.Should().Be(1, "the second list read must come from the scope cache");

        (await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "tenant-a"))!.Value.Should().Be("user-old");
        (await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "tenant-a"))!.Value.Should().Be("user-old");
        userFindCalls.Should().Be(1);

        globalRow = new SettingValue(Guid.NewGuid(), SettingName, SettingScope.Global, string.Empty, "new", false, null);
        await invalidator.InvalidateScopeAsync(SettingScope.Global, string.Empty);

        (await store.GetOrNullAsync(SettingName, SettingScope.Global, string.Empty))!.Value.Should().Be(
            "new",
            "scope invalidation must reach the real item key shape produced by the store");
        globalFindCalls.Should().Be(2);
        (await store.GetListAsync(SettingScope.Global, string.Empty)).Single().Value.Should().Be(
            "new",
            "scope invalidation must also remove the scope list entry");
        globalListCalls.Should().Be(2);

        (await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "tenant-a"))!.Value.Should().Be(
            "user-old");
        userFindCalls.Should().Be(1, "an adjacent tenant partition must stay cached");

        await cache.ClearAsync();
    }

    [Fact]
    public async Task Setting_item_invalidation_removes_only_the_target_item()
    {
        var cache = CreateCache();
        var service = new CrestCacheService(cache, new CrestCacheKeyGenerator());
        var contributor = new SettingCacheKeyContributor();
        var invalidator = new SettingCacheInvalidator(service, contributor);

        var firstCalls = 0;
        var secondCalls = 0;
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync("Sample.First", SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                firstCalls++;
                return new SettingValue(Guid.NewGuid(), "Sample.First", SettingScope.Global, string.Empty, "first", false, null);
            });
        repository
            .Setup(repo => repo.FindAsync("Sample.Second", SettingScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                secondCalls++;
                return new SettingValue(Guid.NewGuid(), "Sample.Second", SettingScope.Global, string.Empty, "second", false, null);
            });

        var store = new SettingStore(repository.Object, Mock.Of<ISettingEncryptionService>(), service, contributor);

        await store.GetOrNullAsync("Sample.First", SettingScope.Global, string.Empty);
        await store.GetOrNullAsync("Sample.Second", SettingScope.Global, string.Empty);
        await store.GetOrNullAsync("Sample.First", SettingScope.Global, string.Empty);
        await store.GetOrNullAsync("Sample.Second", SettingScope.Global, string.Empty);
        firstCalls.Should().Be(1);
        secondCalls.Should().Be(1);

        await invalidator.InvalidateAsync("Sample.First", SettingScope.Global, string.Empty);

        await store.GetOrNullAsync("Sample.First", SettingScope.Global, string.Empty);
        await store.GetOrNullAsync("Sample.Second", SettingScope.Global, string.Empty);
        firstCalls.Should().Be(2, "the invalidated item must be re-read");
        secondCalls.Should().Be(1, "a sibling item in the same scope must stay cached");

        await cache.ClearAsync();
    }

    [Fact]
    public async Task Setting_scope_pattern_escapes_literal_tenant_metacharacters()
    {
        var cache = CreateCache();
        var service = new CrestCacheService(cache, new CrestCacheKeyGenerator());
        var contributor = new SettingCacheKeyContributor();
        var invalidator = new SettingCacheInvalidator(service, contributor);

        var literalRow = new SettingValue(Guid.NewGuid(), SettingName, SettingScope.User, "user-1", "literal-old", false, "t*a");
        var siblingRow = new SettingValue(Guid.NewGuid(), SettingName, SettingScope.User, "user-1", "sibling-old", false, "tXa");
        var literalCalls = 0;
        var siblingCalls = 0;

        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(SettingName, SettingScope.User, "user-1", "t*a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                literalCalls++;
                return literalRow;
            });
        repository
            .Setup(repo => repo.FindAsync(SettingName, SettingScope.User, "user-1", "tXa", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                siblingCalls++;
                return siblingRow;
            });

        var store = new SettingStore(repository.Object, Mock.Of<ISettingEncryptionService>(), service, contributor);

        await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "t*a");
        await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "tXa");
        await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "t*a");
        await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "tXa");

        literalRow = new SettingValue(Guid.NewGuid(), SettingName, SettingScope.User, "user-1", "literal-new", false, "t*a");
        await invalidator.InvalidateScopeAsync(SettingScope.User, "user-1", "t*a");

        (await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "t*a"))!.Value.Should().Be("literal-new");
        literalCalls.Should().Be(2);
        (await store.GetOrNullAsync(SettingName, SettingScope.User, "user-1", "tXa"))!.Value.Should().Be("sibling-old");
        siblingCalls.Should().Be(1, "a glob metacharacter inside a literal tenant id must not match a sibling tenant");

        await cache.ClearAsync();
    }

    [Fact]
    public async Task Feature_scope_invalidation_refreshes_items_and_list_and_preserves_sibling_partitions()
    {
        var cache = CreateCache();
        var service = new CrestCacheService(cache, new CrestCacheKeyGenerator());
        var contributor = new FeatureCacheKeyContributor();
        var invalidator = new FeatureCacheInvalidator(service, contributor);

        var globalRow = new FeatureValue(Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, "old", null);
        var tenantRow = new FeatureValue(Guid.NewGuid(), FeatureName, FeatureScope.Tenant, "tenant-a", "tenant-old", "tenant-a");
        var globalFindCalls = 0;
        var globalListCalls = 0;
        var tenantFindCalls = 0;

        var repository = new Mock<IFeatureRepository>();
        repository
            .Setup(repo => repo.FindAsync(FeatureName, FeatureScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                globalFindCalls++;
                return globalRow;
            });
        repository
            .Setup(repo => repo.FindAsync(FeatureName, FeatureScope.Tenant, "tenant-a", "tenant-a", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                tenantFindCalls++;
                return tenantRow;
            });
        repository
            .Setup(repo => repo.GetListByScopeAsync(FeatureScope.Global, string.Empty, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                globalListCalls++;
                return new List<FeatureValue> { globalRow };
            });

        var store = new FeatureStore(repository.Object, service, contributor);

        (await store.GetOrNullAsync(FeatureName, FeatureScope.Global, string.Empty))!.Value.Should().Be("old");
        (await store.GetOrNullAsync(FeatureName, FeatureScope.Global, string.Empty))!.Value.Should().Be("old");
        globalFindCalls.Should().Be(1);
        (await store.GetListAsync(FeatureScope.Global, string.Empty)).Single().Value.Should().Be("old");
        (await store.GetListAsync(FeatureScope.Global, string.Empty)).Single().Value.Should().Be("old");
        globalListCalls.Should().Be(1);
        (await store.GetOrNullAsync(FeatureName, FeatureScope.Tenant, "tenant-a", "tenant-a"))!.Value.Should().Be("tenant-old");
        (await store.GetOrNullAsync(FeatureName, FeatureScope.Tenant, "tenant-a", "tenant-a"))!.Value.Should().Be("tenant-old");
        tenantFindCalls.Should().Be(1);

        globalRow = new FeatureValue(Guid.NewGuid(), FeatureName, FeatureScope.Global, string.Empty, "new", null);
        await invalidator.InvalidateScopeAsync(FeatureScope.Global, string.Empty);

        (await store.GetOrNullAsync(FeatureName, FeatureScope.Global, string.Empty))!.Value.Should().Be("new");
        globalFindCalls.Should().Be(2);
        (await store.GetListAsync(FeatureScope.Global, string.Empty)).Single().Value.Should().Be("new");
        globalListCalls.Should().Be(2);
        (await store.GetOrNullAsync(FeatureName, FeatureScope.Tenant, "tenant-a", "tenant-a"))!.Value.Should().Be("tenant-old");
        tenantFindCalls.Should().Be(1, "an adjacent tenant partition must stay cached");

        await cache.ClearAsync();
    }

    private static CrestMemoryCache CreateCache()
    {
        return new CrestMemoryCache(new CacheOptions { Prefix = $"Crest-contract-{Guid.NewGuid():N}:" });
    }
}
