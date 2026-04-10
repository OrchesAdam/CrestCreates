using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Settings;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Settings;

public class SettingStoreCacheTests
{
    private readonly List<SettingValue> _settings = new();
    private readonly Mock<ISettingRepository> _settingRepositoryMock = new();
    private readonly ISettingStore _settingStore;
    private readonly ISettingManager _settingManager;
    private int _repositoryGetListCallCount;

    public SettingStoreCacheTests()
    {
        var definitionManager = new SettingDefinitionManager([new CoreSettingDefinitionProvider()]);
        var cacheService = new CrestCacheService(
            new CrestMemoryCache(new CacheOptions
            {
                Prefix = $"SettingStoreCacheTests:{Guid.NewGuid():N}:"
            }),
            new CrestCacheKeyGenerator());
        var cacheKeyContributor = new SettingCacheKeyContributor();
        var encryptionService = new PlainTextEncryptionService();

        _settingRepositoryMock
            .Setup(repository => repository.FindAsync(
                It.IsAny<string>(),
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, SettingScope scope, string providerKey, string? tenantId, CancellationToken _) =>
                _settings.FirstOrDefault(setting =>
                    setting.Name == name &&
                    setting.Scope == scope &&
                    setting.ProviderKey == providerKey &&
                    setting.TenantId == tenantId));

        _settingRepositoryMock
            .Setup(repository => repository.GetListByScopeAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingScope scope, string? providerKey, string? tenantId, CancellationToken _) =>
            {
                _repositoryGetListCallCount++;
                return _settings
                    .Where(setting => setting.Scope == scope)
                    .Where(setting => providerKey is null || setting.ProviderKey == providerKey)
                    .Where(setting => setting.TenantId == tenantId)
                    .OrderBy(setting => setting.Name)
                    .ToList();
            });

        _settingRepositoryMock
            .Setup(repository => repository.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue setting, CancellationToken _) =>
            {
                _settings.Add(setting);
                return setting;
            });

        _settingRepositoryMock
            .Setup(repository => repository.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue setting, CancellationToken _) => setting);

        _settingRepositoryMock
            .Setup(repository => repository.DeleteAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Returns((SettingValue setting, CancellationToken _) =>
            {
                _settings.Remove(setting);
                return Task.CompletedTask;
            });

        _settingStore = new SettingStore(
            _settingRepositoryMock.Object,
            encryptionService,
            cacheService,
            cacheKeyContributor);

        _settingManager = new SettingManager(
            definitionManager,
            _settingRepositoryMock.Object,
            _settingStore,
            encryptionService,
            new SettingValueTypeConverter(),
            new SettingCacheInvalidator(cacheService, cacheKeyContributor));
    }

    [Fact]
    public async Task UpdateAndDelete_ShouldInvalidateScopeCache()
    {
        await _settingManager.SetTenantAsync("App.DisplayName", "tenant-1", "Tenant-A");

        var first = await _settingStore.GetListAsync(SettingScope.Tenant, "tenant-1", "tenant-1");
        var second = await _settingStore.GetListAsync(SettingScope.Tenant, "tenant-1", "tenant-1");

        first.Should().ContainSingle();
        second.Should().ContainSingle();
        _repositoryGetListCallCount.Should().Be(1);

        await _settingManager.SetTenantAsync("App.DisplayName", "tenant-1", "Tenant-B");
        var afterUpdate = await _settingStore.GetListAsync(SettingScope.Tenant, "tenant-1", "tenant-1");

        afterUpdate.Should().ContainSingle();
        afterUpdate[0].Value.Should().Be("Tenant-B");
        _repositoryGetListCallCount.Should().Be(2);

        await _settingManager.RemoveTenantAsync("App.DisplayName", "tenant-1");
        var afterDelete = await _settingStore.GetListAsync(SettingScope.Tenant, "tenant-1", "tenant-1");

        afterDelete.Should().BeEmpty();
        _repositoryGetListCallCount.Should().Be(3);
    }

    [Fact]
    public async Task DifferentTenantAndUser_ShouldNotShareCache()
    {
        await _settingManager.SetTenantAsync("App.DisplayName", "tenant-1", "Tenant-1");
        await _settingManager.SetTenantAsync("App.DisplayName", "tenant-2", "Tenant-2");
        await _settingManager.SetUserAsync("App.DisplayName", "user-1", "User-1", "tenant-1");
        await _settingManager.SetUserAsync("App.DisplayName", "user-2", "User-2", "tenant-1");

        var tenant1 = await _settingStore.GetListAsync(SettingScope.Tenant, "tenant-1", "tenant-1");
        var tenant2 = await _settingStore.GetListAsync(SettingScope.Tenant, "tenant-2", "tenant-2");
        var user1 = await _settingStore.GetListAsync(SettingScope.User, "user-1", "tenant-1");
        var user2 = await _settingStore.GetListAsync(SettingScope.User, "user-2", "tenant-1");

        tenant1[0].Value.Should().Be("Tenant-1");
        tenant2[0].Value.Should().Be("Tenant-2");
        user1[0].Value.Should().Be("User-1");
        user2[0].Value.Should().Be("User-2");
    }

    private sealed class PlainTextEncryptionService : ISettingEncryptionService
    {
        public string Protect(string value) => value;

        public string? Unprotect(string? protectedValue) => protectedValue;

        public string Mask(string? value) => value ?? string.Empty;
    }
}
