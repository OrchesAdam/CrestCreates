using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Permissions;
using CrestCreates.Application.Permissions;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

public class PermissionGrantAppServiceTests
{
    [Fact]
    public async Task GetUserEffectivePermissionsAsync_WithTenantId_OnlyUsesRolesInCurrentTenant()
    {
        var permissionGrantManagerMock = new Mock<IPermissionGrantManager>();
        var roleRepositoryMock = new Mock<IRoleRepository>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns("tenant-a");
        var permissionCheckerMock = new Mock<IPermissionChecker>();
        permissionCheckerMock
            .Setup(checker => checker.CheckAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        userRepositoryMock
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(userId, "librarian", "librarian@test", "tenant-a"));

        roleRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new List<Role>
            {
                new()
                {
                    Name = "Librarian",
                    TenantId = "tenant-a"
                },
                new()
                {
                    Name = "Auditor",
                    TenantId = "tenant-b"
                }
            });

        permissionGrantManagerMock
            .Setup(manager => manager.GetEffectivePermissionsAsync(
                "11111111-1111-1111-1111-111111111111",
                It.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "Librarian" })),
                "tenant-a",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new[] { "Books.View" });

        var service = new PermissionGrantAppService(
            permissionGrantManagerMock.Object,
            roleRepositoryMock.Object,
            userRepositoryMock.Object,
            currentTenantMock.Object,
            permissionCheckerMock.Object);

        var result = await service.GetUserEffectivePermissionsAsync(
            "11111111-1111-1111-1111-111111111111",
            "tenant-a");

        result.Should().BeEquivalentTo(new UserEffectivePermissionsDto
        {
            UserId = "11111111-1111-1111-1111-111111111111",
            TenantId = "tenant-a",
            Permissions = new[] { "Books.View" }
        });
    }

    [Fact]
    public async Task GrantToUserAsync_WithoutManagePermission_DoesNotCallManagerOrUserRepository()
    {
        var (service, manager, users, _, checker) = CreateService();
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Manage))
            .ThrowsAsync(new CrestPermissionException(PermissionManagementPermissions.Manage));

        var action = async () => await service.GrantToUserAsync(
            Guid.NewGuid().ToString(),
            TenantScopedChange("tenant-a", "Books.View"));

        await action.Should().ThrowAsync<CrestPermissionException>();
        manager.VerifyNoOtherCalls();
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GrantToUserAsync_WithManagePermissionAndOwnedUser_Grants()
    {
        var (service, manager, users, _, checker) = CreateService(currentTenantId: "tenant-a");
        var userId = Guid.NewGuid();
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Manage))
            .Returns(Task.CompletedTask);
        users
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(userId, "librarian", "librarian@test", "tenant-a"));
        manager
            .Setup(manager => manager.GrantAsync(It.IsAny<PermissionGrantInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.GrantToUserAsync(userId.ToString(), TenantScopedChange("tenant-a", "Books.View"));

        manager.Verify(
            manager => manager.GrantAsync(
                It.Is<PermissionGrantInfo>(grant =>
                    grant.ProviderType == PermissionGrantProviderType.User &&
                    grant.ProviderKey == userId.ToString() &&
                    grant.Scope == PermissionGrantScope.Tenant &&
                    grant.TenantId == "tenant-a"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantToUserAsync_WithManagePermissionButForeignTenantUser_DoesNotCallManager()
    {
        var (service, manager, users, _, checker) = CreateService(currentTenantId: "tenant-a");
        var userId = Guid.NewGuid();
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Manage))
            .Returns(Task.CompletedTask);
        users
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(userId, "foreign", "foreign@test", "tenant-b"));

        var action = async () => await service.GrantToUserAsync(
            userId.ToString(),
            TenantScopedChange("tenant-a", "Books.View"));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*不属于租户*");
        manager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeFromUserAsync_CrossTenantWithoutPermission_DoesNotCallManager()
    {
        var (service, manager, _, _, checker) = CreateService(currentTenantId: "tenant-a");
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Manage))
            .ThrowsAsync(new CrestPermissionException(PermissionManagementPermissions.Manage));

        var action = async () => await service.RevokeFromUserAsync(
            Guid.NewGuid().ToString(),
            TenantScopedChange("tenant-b", "Books.View"));

        await action.Should().ThrowAsync<CrestPermissionException>();
        manager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeFromUserAsync_CrossTenantWithPermission_DefersScopeCheckToManager()
    {
        var (service, manager, _, _, checker) = CreateService(currentTenantId: "tenant-a");
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Manage))
            .Returns(Task.CompletedTask);
        manager
            .Setup(manager => manager.RevokeAsync(It.IsAny<PermissionGrantInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("权限撤销被拒绝: 不属于当前租户"));

        var action = async () => await service.RevokeFromUserAsync(
            Guid.NewGuid().ToString(),
            TenantScopedChange("tenant-b", "Books.View"));

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*权限撤销被拒绝*");
        manager.Verify(
            manager => manager.RevokeAsync(It.IsAny<PermissionGrantInfo>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserGrantsAsync_WithoutReadPermission_DoesNotCallManager()
    {
        var (service, manager, _, _, checker) = CreateService();
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Read))
            .ThrowsAsync(new CrestPermissionException(PermissionManagementPermissions.Read));

        var action = async () => await service.GetUserGrantsAsync(Guid.NewGuid().ToString());

        await action.Should().ThrowAsync<CrestPermissionException>();
        manager.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetUserEffectivePermissionsAsync_WithForeignTenantId_IsRejectedBeforeLookup()
    {
        var (service, manager, users, _, checker) = CreateService(currentTenantId: "tenant-a");
        checker
            .Setup(c => c.CheckAsync(PermissionManagementPermissions.Read))
            .Returns(Task.CompletedTask);

        var action = async () => await service.GetUserEffectivePermissionsAsync(
            Guid.NewGuid().ToString(),
            "tenant-b");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*只能读取本租户*");
        manager.VerifyNoOtherCalls();
        users.VerifyNoOtherCalls();
    }

    [Fact]
    public void AddPermissionManagement_RegistersPermissionDefinitions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrestAuthorization();
        services.AddPermissionManagement();

        using var provider = services.BuildServiceProvider();
        var definitions = provider.GetRequiredService<IPermissionDefinitionManager>();

        definitions.GetOrNull(PermissionManagementPermissions.Read).Should().NotBeNull();
        definitions.GetOrNull(PermissionManagementPermissions.Manage).Should().NotBeNull();
    }

    private static (
        PermissionGrantAppService Service,
        Mock<IPermissionGrantManager> Manager,
        Mock<IUserRepository> Users,
        Mock<IRoleRepository> Roles,
        Mock<IPermissionChecker> Checker) CreateService(string? currentTenantId = null)
    {
        var manager = new Mock<IPermissionGrantManager>();
        var users = new Mock<IUserRepository>();
        var roles = new Mock<IRoleRepository>();
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(currentTenant => currentTenant.Id).Returns(currentTenantId);
        var checker = new Mock<IPermissionChecker>();

        return (
            new PermissionGrantAppService(manager.Object, roles.Object, users.Object, tenant.Object, checker.Object),
            manager,
            users,
            roles,
            checker);
    }

    private static PermissionGrantChangeDto TenantScopedChange(string tenantId, string permissionName) => new()
    {
        PermissionName = permissionName,
        Scope = PermissionGrantScope.Tenant,
        TenantId = tenantId
    };
}
