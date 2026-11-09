using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Application.Settings;
using CrestCreates.Authorization.Abstractions;
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
