using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Features;

public class FeatureManagementTests
{
    private readonly List<FeatureValue> _features = new();
    private readonly Mock<IFeatureRepository> _featureRepositoryMock = new();
    private readonly IFeatureDefinitionManager _featureDefinitionManager;
    private readonly IFeatureStore _featureStore;
    private readonly IFeatureManager _featureManager;
    private readonly IFeatureValueResolver _featureValueResolver;

    public FeatureManagementTests()
    {
        _featureDefinitionManager = new FeatureDefinitionManager(
            [new CoreFeatureDefinitionProvider()]);

        ConfigureRepository();

        var cacheService = new CrestCacheService(
            new CrestMemoryCache(new CacheOptions
            {
                Prefix = $"FeatureTests:{Guid.NewGuid():N}:"
            }),
            new CrestCacheKeyGenerator());
        var cacheKeyContributor = new FeatureCacheKeyContributor();

        _featureStore = new FeatureStore(
            _featureRepositoryMock.Object,
            cacheService,
            cacheKeyContributor);

        _featureValueResolver = new FeatureValueResolver(
            _featureDefinitionManager,
            _featureStore);

        _featureManager = new FeatureManager(
            _featureDefinitionManager,
            _featureRepositoryMock.Object,
            _featureStore,
            new FeatureValueTypeConverter(),
            new FeatureCacheInvalidator(cacheService, cacheKeyContributor));
    }

    [Fact]
    public async Task GetOrNullAsync_TenantOverridesGlobalOverridesDefault()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

        var globalResult = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled");
        globalResult.Value.Should().Be("false");
        globalResult.Scope.Should().Be(FeatureScope.Global);

        var tenantResult = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
        tenantResult.Value.Should().Be("true");
        tenantResult.Scope.Should().Be(FeatureScope.Tenant);
    }

    [Fact]
    public async Task SetGlobalAsync_WithUndefinedFeature_Throws()
    {
        var action = async () => await _featureManager.SetGlobalAsync("Unknown.Feature", "value");

        var exception = await action.Should().ThrowAsync<CrestBusinessException>();
        exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.UndefinedFeature);
    }

    [Fact]
    public async Task SetGlobalAsync_WithInvalidBoolValue_Throws()
    {
        var action = async () => await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "abc");

        var exception = await action.Should().ThrowAsync<CrestBusinessException>();
        exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task UnknownFeature_ShouldThrowBusinessException()
    {
        var action = async () => await _featureManager.SetGlobalAsync("Unknown.Feature", "true");

        var exception = await action.Should().ThrowAsync<CrestBusinessException>();
        exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.UndefinedFeature);
        exception.Which.Message.Should().Contain("Unknown.Feature");
    }

    [Fact]
    public async Task InvalidFeatureValue_ShouldThrowBusinessException()
    {
        var action = async () => await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "abc");

        var exception = await action.Should().ThrowAsync<CrestBusinessException>();
        exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.InvalidValue);
        exception.Which.Message.Should().Contain("Identity.UserCreationEnabled");
    }

    [Fact]
    public async Task SetTenantAsync_WithSupportedScope_WorksCorrectly()
    {
        var action = async () => await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "false");

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveOverride_ShouldFallbackToNextScope()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

        await _featureManager.RemoveTenantAsync("Identity.UserCreationEnabled", "tenant-1");
        var afterTenantRemoval = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
        afterTenantRemoval.Value.Should().Be("false");
        afterTenantRemoval.Scope.Should().Be(FeatureScope.Global);

        await _featureManager.RemoveGlobalAsync("Identity.UserCreationEnabled");
        var afterGlobalRemoval = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
        afterGlobalRemoval.Value.Should().Be("true");
        afterGlobalRemoval.Scope.Should().BeNull();
    }

    [Fact]
    public async Task BoolFeature_IsEnabled_WorksCorrectly()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "true");
        var checker = CreateFeatureChecker(null);
        var isEnabled = await checker.IsEnabledAsync("Identity.UserCreationEnabled");
        isEnabled.Should().BeTrue();

        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        isEnabled = await checker.IsEnabledAsync("Identity.UserCreationEnabled");
        isEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_WithExplicitTenantId_UsesThatTenantNotCurrentTenant()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

        var checkerWithTenant1Context = CreateFeatureChecker("tenant-1");
        var checkerWithTenant2Context = CreateFeatureChecker("tenant-2");

        var tenant1Result = await checkerWithTenant1Context.IsEnabledAsync("tenant-1", "Identity.UserCreationEnabled");
        tenant1Result.Should().BeTrue();

        var tenant2Result = await checkerWithTenant2Context.IsEnabledAsync("tenant-2", "Identity.UserCreationEnabled");
        tenant2Result.Should().BeFalse();
    }

    [Fact]
    public async Task IntFeature_StoresAndRetrievesCorrectly()
    {
        await _featureManager.SetGlobalAsync("Storage.MaxFileCount", "500");

        var result = await _featureValueResolver.ResolveAsync("Storage.MaxFileCount");
        result.Value.Should().Be("500");
        result.Scope.Should().Be(FeatureScope.Global);
    }

    [Fact]
    public async Task StringFeature_StoresAndRetrievesCorrectly()
    {
        await _featureManager.SetGlobalAsync("Ui.Theme", "Dark");

        var result = await _featureValueResolver.ResolveAsync("Ui.Theme");
        result.Value.Should().Be("Dark");
        result.Scope.Should().Be(FeatureScope.Global);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllDefinitions()
    {
        var allFeatures = await _featureValueResolver.ResolveAllAsync();
        allFeatures.Should().NotBeEmpty();
        allFeatures.Any(f => f.Name == "Identity.UserCreationEnabled").Should().BeTrue();
        allFeatures.Any(f => f.Name == "Storage.MaxFileCount").Should().BeTrue();
        allFeatures.Any(f => f.Name == "Ui.Theme").Should().BeTrue();
    }

    [Fact]
    public async Task GetScopedValuesAsync_ReturnsOnlySupportedScope()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        await _featureManager.SetTenantAsync("Storage.MaxFileCount", "tenant-1", "200");

        var globalValues = await _featureManager.GetScopedValuesAsync(FeatureScope.Global, string.Empty);
        globalValues.Should().ContainSingle(v => v.Name == "Identity.UserCreationEnabled");

        var tenantValues = await _featureManager.GetScopedValuesAsync(FeatureScope.Tenant, "tenant-1", null, "tenant-1");
        tenantValues.Should().ContainSingle(v => v.Name == "Storage.MaxFileCount");
    }

    [Fact]
    public async Task SetTenantAsync_WithGlobalOnlyFeature_ShouldThrowBusinessException()
    {
        var definitionManager = new FeatureDefinitionManager(new IFeatureDefinitionProvider[]
        {
            new GlobalOnlyFeatureProvider()
        });

        var features = new List<FeatureValue>();
        var repoMock = new Mock<IFeatureRepository>();
        ConfigureInMemoryRepo(repoMock, features);

        var cacheService = new CrestCacheService(
            new CrestMemoryCache(new CacheOptions { Prefix = $"FeatureScope:{Guid.NewGuid():N}:" }),
            new CrestCacheKeyGenerator());
        var cacheKeyContributor = new FeatureCacheKeyContributor();
        var store = new FeatureStore(repoMock.Object, cacheService, cacheKeyContributor);

        var manager = new FeatureManager(
            definitionManager,
            repoMock.Object,
            store,
            new FeatureValueTypeConverter(),
            new FeatureCacheInvalidator(cacheService, cacheKeyContributor));

        var action = async () => await manager.SetTenantAsync("Global.Only", "tenant-1", "true");

        var exception = await action.Should().ThrowAsync<CrestBusinessException>();
        exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.UnsupportedScope);
    }

    [Fact]
    public async Task SetTenantAsync_CalledTwice_ShouldUpdateExistingRow()
    {
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "false");

        _features.Where(feature =>
                feature.Name == "Identity.UserCreationEnabled" &&
                feature.Scope == FeatureScope.Tenant &&
                feature.TenantId == "tenant-1")
            .Should()
            .ContainSingle()
            .Which.Value.Should().Be("false");
    }

    [Fact]
    public async Task SetTenantFeature_ShouldInvalidateOnlyThatTenantCache()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

        var tenant1Before = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
        var tenant2Before = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-2");

        tenant1Before.Value.Should().Be("true");
        tenant2Before.Value.Should().Be("false");

        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "false");

        var tenant1After = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
        var tenant2After = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-2");

        tenant1After.Value.Should().Be("false");
        tenant2After.Value.Should().Be("false");
    }

    [Fact]
    public async Task GetCurrentTenantValuesAsync_ShouldReturnResolvedDefinitions()
    {
        await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
        await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(x => x.Id).Returns("tenant-1");

        var appService = new FeatureAppService(
            _featureManager,
            new FeatureProvider(
                _featureDefinitionManager,
                _featureValueResolver,
                new FeatureValueTypeConverter(),
                currentTenant.Object),
            _featureValueResolver,
            currentTenant.Object,
            new FeatureValueAppServiceMapper());

        var values = await appService.GetCurrentTenantValuesAsync();

        values.Should().Contain(value =>
            value.Name == "Identity.UserCreationEnabled" &&
            value.Value == "true" &&
            value.Scope == FeatureScope.Tenant &&
            value.TenantId == "tenant-1");

        values.Should().Contain(value => value.Name == "Storage.MaxFileCount");
    }

    private sealed class GlobalOnlyFeatureProvider : IFeatureDefinitionProvider
    {
        public void Define(FeatureDefinitionContext context)
        {
            context.GetOrAddGroup("Global", "Global")
                .AddDefinition(
                    "Global.Only",
                    "Global Only",
                    "Only global scope is supported",
                    "false",
                    FeatureValueType.Bool,
                    true,
                    true,
                    FeatureScope.Global);
        }
    }

    private static void ConfigureInMemoryRepo(Mock<IFeatureRepository> repoMock, List<FeatureValue> features)
    {
        repoMock
            .Setup(repository => repository.FindAsync(
                It.IsAny<string>(),
                It.IsAny<FeatureScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, FeatureScope scope, string providerKey, string? tenantId, CancellationToken _) =>
                features.FirstOrDefault(feature =>
                    feature.Name == name &&
                    feature.Scope == scope &&
                    feature.ProviderKey == providerKey &&
                    feature.TenantId == tenantId));

        repoMock
            .Setup(repository => repository.GetListByScopeAsync(
                It.IsAny<FeatureScope>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeatureScope scope, string? providerKey, string? tenantId, CancellationToken _) =>
                features
                    .Where(feature => feature.Scope == scope)
                    .Where(feature => providerKey is null || feature.ProviderKey == providerKey)
                    .Where(feature => tenantId == null || feature.TenantId == tenantId)
                    .OrderBy(feature => feature.Name)
                    .ToList());

        repoMock
            .Setup(repository => repository.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeatureValue feature, CancellationToken _) =>
            {
                features.Add(feature);
                return feature;
            });

        repoMock
            .Setup(repository => repository.UpdateAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FeatureValue feature, CancellationToken _) => feature);

        repoMock
            .Setup(repository => repository.DeleteAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Returns((FeatureValue feature, CancellationToken _) =>
            {
                features.Remove(feature);
                return Task.CompletedTask;
            });
    }

    private void ConfigureRepository()
    {
        ConfigureInMemoryRepo(_featureRepositoryMock, _features);
    }

    private IFeatureChecker CreateFeatureChecker(string? tenantId)
    {
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns(tenantId ?? string.Empty);
        currentTenantMock.SetupGet(currentTenant => currentTenant.Tenant).Returns((ITenantInfo?)null);

        var featureProvider = new FeatureProvider(
            _featureDefinitionManager,
            _featureValueResolver,
            new FeatureValueTypeConverter(),
            currentTenantMock.Object);

        return new FeatureChecker(
            _featureDefinitionManager,
            _featureValueResolver,
            currentTenantMock.Object);
    }
}
