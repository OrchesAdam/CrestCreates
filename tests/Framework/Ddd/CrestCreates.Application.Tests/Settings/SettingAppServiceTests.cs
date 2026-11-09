using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Settings;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Settings;
using CrestCreates.Domain.Shared.Settings;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Settings;

public class SettingAppServiceTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    private readonly Mock<ISettingProvider> _settingProviderMock = new();
    private readonly Mock<ISettingManager> _settingManagerMock = new();
    private readonly Mock<ISettingDefinitionManager> _settingDefinitionManagerMock = new();
    private readonly Mock<ICurrentTenant> _currentTenantMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IPermissionChecker> _permissionCheckerMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<ISettingEncryptionService> _encryptionMock = new();
    private readonly SettingValueAppServiceMapper _mapper;
    private readonly SettingAppService _settingAppService;

    public SettingAppServiceTests()
    {
        _currentUserMock.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUserMock.SetupGet(user => user.Id).Returns("11111111-1111-1111-1111-111111111111");
        _currentUserMock.SetupGet(user => user.IsSuperAdmin).Returns(false);
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(string.Empty);

        _permissionCheckerMock
            .Setup(checker => checker.CheckAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        _settingProviderMock
            .Setup(provider => provider.GetAllAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ResolvedSettingValue>());
        _settingManagerMock
            .Setup(manager => manager.GetScopedValuesAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SettingValueEntry>());
        _settingDefinitionManagerMock
            .Setup(manager => manager.GetAll())
            .Returns(Array.Empty<SettingDefinition>());

        _encryptionMock.Setup(encryption => encryption.Mask(It.IsAny<string?>())).Returns("***");
        _mapper = new SettingValueAppServiceMapper(_settingDefinitionManagerMock.Object, _encryptionMock.Object);
        _settingAppService = new SettingAppService(
            _settingProviderMock.Object,
            _settingManagerMock.Object,
            _settingDefinitionManagerMock.Object,
            _mapper,
            _currentTenantMock.Object,
            _currentUserMock.Object,
            _permissionCheckerMock.Object,
            _userRepositoryMock.Object);
    }

    [Fact]
    public async Task Anonymous_self_read_is_rejected_before_any_value_is_resolved()
    {
        _currentUserMock.SetupGet(user => user.IsAuthenticated).Returns(false);

        var act = async () => await _settingAppService.GetCurrentValuesAsync();

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _settingProviderMock.Verify(
            provider => provider.GetAllAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an anonymous caller must not read settings");
    }

    [Fact]
    public async Task Management_access_without_permission_returns_nothing_and_writes_nothing()
    {
        _permissionCheckerMock
            .Setup(checker => checker.CheckAsync(SettingManagementPermissions.Read))
            .ThrowsAsync(new CrestPermissionException(SettingManagementPermissions.Read));
        _permissionCheckerMock
            .Setup(checker => checker.CheckAsync(SettingManagementPermissions.Manage))
            .ThrowsAsync(new CrestPermissionException(SettingManagementPermissions.Manage));

        var readGlobal = async () => await _settingAppService.GetGlobalValuesAsync();
        var readTenant = async () => await _settingAppService.GetTenantValuesAsync(TenantB);
        var writeGlobal = async () => await _settingAppService.UpdateGlobalAsync("Any", new UpdateSettingValueDto { Value = "x" });
        var writeTenant = async () => await _settingAppService.UpdateTenantAsync(TenantB, "Any", new UpdateSettingValueDto { Value = "x" });

        await readGlobal.Should().ThrowAsync<CrestPermissionException>();
        await readTenant.Should().ThrowAsync<CrestPermissionException>();
        await writeGlobal.Should().ThrowAsync<CrestPermissionException>();
        await writeTenant.Should().ThrowAsync<CrestPermissionException>();

        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoWrites();
    }

    [Fact]
    public async Task Tenant_admin_cannot_read_or_write_another_tenant_by_tenant_or_user_id()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var tenantBUser = Guid.NewGuid();
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(tenantBUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(tenantBUser, "tenant-b-user", "tenant-b@test", TenantB));

        var readTenant = async () => await _settingAppService.GetTenantValuesAsync(TenantB);
        var writeTenant = async () =>
            await _settingAppService.UpdateTenantAsync(TenantB, "Any", new UpdateSettingValueDto { Value = "x" });
        var readUser = async () => await _settingAppService.GetUserValuesAsync(tenantBUser.ToString(), TenantB);
        var writeUser = async () =>
            await _settingAppService.UpdateUserAsync(tenantBUser.ToString(), "Any", new UpdateSettingValueDto { Value = "x" }, TenantB);

        await readTenant.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户*");
        await writeTenant.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户*");
        await readUser.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户*");
        await writeUser.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户*");

        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoWrites();
    }

    [Fact]
    public async Task Host_with_permission_can_read_and_write_another_tenant()
    {
        var tenantBUser = Guid.NewGuid();
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(tenantBUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(tenantBUser, "tenant-b-user", "tenant-b@test", TenantB));

        await _settingAppService.GetTenantValuesAsync(TenantB);
        await _settingAppService.UpdateTenantAsync(TenantB, "Tenant.Setting", new UpdateSettingValueDto { Value = "v" });
        await _settingAppService.GetUserValuesAsync(tenantBUser.ToString(), TenantB);
        await _settingAppService.UpdateUserAsync(
            tenantBUser.ToString(), "User.Setting", new UpdateSettingValueDto { Value = "v" }, TenantB);

        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(SettingScope.Tenant, TenantB, null, TenantB, It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.SetTenantAsync("Tenant.Setting", TenantB, "v", It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(SettingScope.User, tenantBUser.ToString(), null, TenantB, It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.SetUserAsync("User.Setting", tenantBUser.ToString(), "v", TenantB, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tenant_context_cannot_manage_global_settings_without_superadmin()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);

        var readGlobal = async () => await _settingAppService.GetGlobalValuesAsync();
        var writeGlobal = async () => await _settingAppService.UpdateGlobalAsync("Any", new UpdateSettingValueDto { Value = "x" });
        var deleteGlobal = async () => await _settingAppService.DeleteGlobalAsync("Any");

        await readGlobal.Should().ThrowAsync<InvalidOperationException>().WithMessage("*全局设置*");
        await writeGlobal.Should().ThrowAsync<InvalidOperationException>().WithMessage("*全局设置*");
        await deleteGlobal.Should().ThrowAsync<InvalidOperationException>().WithMessage("*全局设置*");

        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoWrites();
    }

    [Fact]
    public async Task Super_admin_can_manage_global_settings_from_a_tenant_context()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        _currentUserMock.SetupGet(user => user.IsSuperAdmin).Returns(true);

        await _settingAppService.UpdateGlobalAsync("Global.Setting", new UpdateSettingValueDto { Value = "v" });

        _settingManagerMock.Verify(
            manager => manager.SetGlobalAsync("Global.Setting", "v", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" tenant-a ")]
    public async Task Omitted_tenant_resolves_to_the_current_tenant_for_user_reads_writes_and_deletes(string? tenantId)
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var userId = Guid.NewGuid();
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(userId, "tenant-a-user", "tenant-a@test", TenantA));

        await _settingAppService.GetUserValuesAsync(userId.ToString(), tenantId);
        await _settingAppService.UpdateUserAsync(
            userId.ToString(), "User.Setting", new UpdateSettingValueDto { Value = "v" }, tenantId);
        await _settingAppService.DeleteUserAsync(userId.ToString(), "User.Setting", tenantId);

        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(
                SettingScope.User, userId.ToString(), null, TenantA, It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.SetUserAsync(
                "User.Setting", userId.ToString(), "v", TenantA, It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.RemoveUserAsync(
                "User.Setting", userId.ToString(), TenantA, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tenant_cannot_delete_another_tenant_user_and_nothing_is_written()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);

        var act = async () => await _settingAppService.DeleteUserAsync(
            Guid.NewGuid().ToString(), "User.Setting", " tenant-b ");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户*");
        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoWrites();
    }

    [Fact]
    public async Task Host_explicit_tenant_user_delete_targets_the_resolved_partition()
    {
        var tenantBUser = Guid.NewGuid();
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(tenantBUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(tenantBUser, "tenant-b-user", "tenant-b@test", TenantB));

        await _settingAppService.DeleteUserAsync(tenantBUser.ToString(), "User.Setting", " tenant-b ");

        _settingManagerMock.Verify(
            manager => manager.RemoveUserAsync(
                "User.Setting", tenantBUser.ToString(), TenantB, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Host_omitted_tenant_keeps_the_host_partition_instead_of_inventing_a_tenant()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(string.Empty);
        var hostUser = Guid.NewGuid();
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(hostUser, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(hostUser, "host-user", "host@test", string.Empty));

        await _settingAppService.GetUserValuesAsync(hostUser.ToString());
        await _settingAppService.UpdateUserAsync(
            hostUser.ToString(), "User.Setting", new UpdateSettingValueDto { Value = "v" });
        await _settingAppService.DeleteUserAsync(hostUser.ToString(), "User.Setting");

        _settingManagerMock.Verify(
            manager => manager.GetScopedValuesAsync(
                SettingScope.User, hostUser.ToString(), null, null, It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.SetUserAsync(
                "User.Setting", hostUser.ToString(), "v", null, It.IsAny<CancellationToken>()),
            Times.Once);
        _settingManagerMock.Verify(
            manager => manager.RemoveUserAsync(
                "User.Setting", hostUser.ToString(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Omitted_tenant_targets_only_the_authorized_partition_in_the_real_store()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var userId = Guid.NewGuid();
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(userId, "tenant-a-user", "tenant-a@test", TenantA));

        var definition = new SettingDefinition("User.Setting", "User", scopes: SettingScope.User);
        var definitions = new Mock<ISettingDefinitionManager>();
        definitions.Setup(manager => manager.GetAll()).Returns(new[] { definition });
        definitions.Setup(manager => manager.GetOrNull("User.Setting")).Returns(definition);
        definitions.Setup(manager => manager.GetOrNull("User.Setting ")).Returns(definition);
        var encryption = new Mock<ISettingEncryptionService>();

        var settings = new List<SettingValue>
        {
            new(Guid.NewGuid(), "User.Setting", SettingScope.User, userId.ToString(), "tenant-a-value", false, TenantA),
            new(Guid.NewGuid(), "User.Setting", SettingScope.User, userId.ToString(), "host-value", false, null),
        };
        var repository = new Mock<ISettingRepository>();
        repository
            .Setup(repo => repo.FindAsync(
                It.IsAny<string>(),
                It.IsAny<SettingScope>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, SettingScope scope, string providerKey, string? tenantId, CancellationToken _) =>
                settings.SingleOrDefault(setting =>
                    setting.Name == name &&
                    setting.Scope == scope &&
                    setting.ProviderKey == providerKey &&
                    setting.TenantId == tenantId));
        repository
            .Setup(repo => repo.GetListByScopeAsync(
                It.IsAny<SettingScope>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingScope scope, string? providerKey, string? tenantId, CancellationToken _) =>
                settings
                    .Where(setting =>
                        setting.Scope == scope &&
                        (providerKey == null || setting.ProviderKey == providerKey) &&
                        setting.TenantId == tenantId)
                    .ToList());
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue setting, CancellationToken _) =>
            {
                settings.Add(setting);
                return setting;
            });
        repository
            .Setup(repo => repo.UpdateAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SettingValue setting, CancellationToken _) => setting);
        repository
            .Setup(repo => repo.DeleteAsync(It.IsAny<SettingValue>(), It.IsAny<CancellationToken>()))
            .Callback((SettingValue setting, CancellationToken _) => settings.Remove(setting))
            .Returns(Task.CompletedTask);

        var removedKeys = new List<string>();
        var cache = new Mock<ICrestCacheService>();
        cache
            .Setup(service => service.RemoveAsync(It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback((string prefix, object[] parts) => removedKeys.Add(prefix + "|" + string.Join(":", parts)))
            .Returns(Task.CompletedTask);

        var contributor = new SettingCacheKeyContributor();
        var store = new SettingStore(repository.Object, encryption.Object, cache.Object, contributor);
        var manager = new SettingManager(
            definitions.Object,
            repository.Object,
            store,
            encryption.Object,
            new SettingValueTypeConverter(),
            new SettingCacheInvalidator(cache.Object, contributor));
        var service = new SettingAppService(
            _settingProviderMock.Object,
            manager,
            definitions.Object,
            new SettingValueAppServiceMapper(definitions.Object, encryption.Object),
            _currentTenantMock.Object,
            _currentUserMock.Object,
            _permissionCheckerMock.Object,
            _userRepositoryMock.Object);

        var read = await service.GetUserValuesAsync(userId.ToString());
        read.Single().Value.Should().Be("tenant-a-value");

        await service.UpdateUserAsync(
            userId.ToString(), "User.Setting", new UpdateSettingValueDto { Value = "updated-a" });
        settings.Single(setting => setting.TenantId == TenantA).Value.Should().Be("updated-a");
        settings.Single(setting => setting.TenantId == null).Value.Should().Be("host-value");

        await service.DeleteUserAsync(userId.ToString(), "User.Setting");
        settings.Should().NotContain(setting => setting.TenantId == TenantA);
        settings.Single(setting => setting.TenantId == null).Value.Should().Be("host-value");

        removedKeys.Should().NotBeEmpty();
        Assert.All(removedKeys, key => Assert.Contains(TenantA, key, StringComparison.Ordinal));
        Assert.DoesNotContain(removedKeys, key => key.Contains(":none", StringComparison.Ordinal));
    }

    [Fact]
    public void Encrypted_setting_values_are_never_returned_in_plaintext()
    {
        var definition = new SettingDefinition(
            "Secret.Setting",
            "Secret",
            isEncrypted: true,
            scopes: SettingScope.User);
        _settingDefinitionManagerMock
            .Setup(manager => manager.GetOrNull("Secret.Setting"))
            .Returns(definition);
        _encryptionMock.Setup(encryption => encryption.Mask("plaintext")).Returns("p***");

        var dto = _mapper.Map(
            new SettingValueEntry
            {
                Name = "Secret.Setting",
                Value = "plaintext",
                Scope = SettingScope.User,
                ProviderKey = "user-1",
                TenantId = TenantA,
            },
            "Secret.Setting");

        dto.Value.Should().BeNull("encrypted values must not be returned through the management DTO");
        dto.MaskedValue.Should().Be("p***");
    }

    private void VerifyNoWrites()
    {
        _settingManagerMock.Verify(
            manager => manager.SetGlobalAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settingManagerMock.Verify(
            manager => manager.SetTenantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settingManagerMock.Verify(
            manager => manager.SetUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settingManagerMock.Verify(
            manager => manager.RemoveGlobalAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settingManagerMock.Verify(
            manager => manager.RemoveTenantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settingManagerMock.Verify(
            manager => manager.RemoveUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
