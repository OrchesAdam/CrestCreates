using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Settings;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Settings;

public class SettingManagementTests
{
    private readonly List<SettingValue> _settings = new();
    private readonly Mock<ISettingRepository> _settingRepositoryMock = new();
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingStore _settingStore;
    private readonly ISettingManager _settingManager;
    private readonly ISettingValueResolver _settingValueResolver;

    public SettingManagementTests()
    {
        _settingDefinitionManager = new SettingDefinitionManager(
            [new CoreSettingDefinitionProvider()]);

        ConfigureRepository();

        var cacheService = new CrestCacheService(
            new CrestMemoryCache(new CacheOptions
            {
                Prefix = $"SettingTests:{Guid.NewGuid():N}:"
            }),
            new CrestCacheKeyGenerator());
        var cacheKeyContributor = new SettingCacheKeyContributor();
        var encryptionService = new FakeSettingEncryptionService();

        _settingStore = new SettingStore(
            _settingRepositoryMock.Object,
            encryptionService,
            cacheService,
            cacheKeyContributor);

        _settingValueResolver = new SettingValueResolver(
            _settingDefinitionManager,
            _settingStore);

        _settingManager = new SettingManager(
            _settingDefinitionManager,
            _settingRepositoryMock.Object,
            _settingStore,
            encryptionService,
            new SettingValueTypeConverter(),
            new SettingCacheInvalidator(cacheService, cacheKeyContributor));
    }

    [Fact]
    public async Task GetOrNullAsync_FollowsUserTenantGlobalDefaultPriority()
    {
        await _settingManager.SetGlobalAsync("App.DisplayName", "GlobalName");
        await _settingManager.SetTenantAsync("App.DisplayName", "tenant-1", "TenantName");
        await _settingManager.SetUserAsync("App.DisplayName", "user-1", "UserName", "tenant-1");

        var provider = CreateSettingProvider("tenant-1", "user-1");

        var result = await provider.GetOrNullAsync("App.DisplayName");

        result.Should().Be("UserName");
    }

    [Fact]
    public async Task SetGlobalAsync_WithUndefinedSetting_Throws()
    {
        var action = async () => await _settingManager.SetGlobalAsync("Unknown.Setting", "value");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*未定义*");
    }

    [Fact]
    public async Task SetGlobalAsync_WithInvalidBoolValue_Throws()
    {
        var action = async () => await _settingManager.SetGlobalAsync("App.IsRegistrationEnabled", "abc");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Bool*");
    }

    [Fact]
    public async Task SetTenantAsync_WithUnsupportedScope_Throws()
    {
        var action = async () => await _settingManager.SetTenantAsync("Security.Password.MinLength", "tenant-1", "12");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*不支持作用域*");
    }

    [Fact]
    public async Task EncryptedSetting_CanPersistEncryptedAndReadPlaintext()
    {
        await _settingManager.SetTenantAsync("Storage.SecretKey", "tenant-1", "Secret-123");

        _settings.Should().ContainSingle();
        _settings[0].Value.Should().NotBe("Secret-123");

        var resolved = await _settingValueResolver.ResolveAsync("Storage.SecretKey", "tenant-1");

        resolved.Value.Should().Be("Secret-123");
        resolved.Scope.Should().Be(SettingScope.Tenant);
    }

    [Fact]
    public async Task RemoveOverride_ShouldFallbackToNextScope()
    {
        await _settingManager.SetGlobalAsync("App.DisplayName", "GlobalName");
        await _settingManager.SetTenantAsync("App.DisplayName", "tenant-1", "TenantName");
        await _settingManager.SetUserAsync("App.DisplayName", "user-1", "UserName", "tenant-1");

        await _settingManager.RemoveUserAsync("App.DisplayName", "user-1", "tenant-1");
        var afterUserRemoval = await _settingValueResolver.ResolveAsync("App.DisplayName", "tenant-1", "user-1");
        afterUserRemoval.Value.Should().Be("TenantName");

        await _settingManager.RemoveTenantAsync("App.DisplayName", "tenant-1");
        var afterTenantRemoval = await _settingValueResolver.ResolveAsync("App.DisplayName", "tenant-1", "user-1");
        afterTenantRemoval.Value.Should().Be("GlobalName");

        await _settingManager.RemoveGlobalAsync("App.DisplayName");
        var afterGlobalRemoval = await _settingValueResolver.ResolveAsync("App.DisplayName", "tenant-1", "user-1");
        afterGlobalRemoval.Value.Should().Be("CrestCreates");
        afterGlobalRemoval.Scope.Should().BeNull();
    }

    private void ConfigureRepository()
    {
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
                _settings
                    .Where(setting => setting.Scope == scope)
                    .Where(setting => providerKey is null || setting.ProviderKey == providerKey)
                    .Where(setting => setting.TenantId == tenantId)
                    .OrderBy(setting => setting.Name)
                    .ToList());

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
    }

    private ISettingProvider CreateSettingProvider(string tenantId, string userId)
    {
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns(tenantId);
        currentTenantMock.SetupGet(currentTenant => currentTenant.Tenant).Returns((ITenantInfo?)null);

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(currentUser => currentUser.Id).Returns(userId);
        currentUserMock.SetupGet(currentUser => currentUser.IsAuthenticated).Returns(true);

        return new SettingProvider(
            _settingDefinitionManager,
            _settingValueResolver,
            new SettingValueTypeConverter(),
            currentTenantMock.Object,
            currentUserMock.Object);
    }

    private sealed class FakeSettingEncryptionService : ISettingEncryptionService
    {
        public string Protect(string value)
        {
            return $"enc:{value}";
        }

        public string? Unprotect(string? protectedValue)
        {
            return protectedValue?.StartsWith("enc:", StringComparison.Ordinal) == true
                ? protectedValue[4..]
                : protectedValue;
        }

        public string Mask(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length <= 4 ? "***" : value[..2] + "***" + value[^2..];
        }
    }
}
