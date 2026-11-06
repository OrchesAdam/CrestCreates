using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

public class PermissionGrantManagerTests
{
    private readonly Mock<IPermissionGrantRepository> _permissionGrantRepositoryMock;
    private readonly Mock<IPermissionGrantStore> _permissionGrantStoreMock;
    private readonly PermissionGrantManager _permissionGrantManager;

    public PermissionGrantManagerTests()
    {
        _permissionGrantRepositoryMock = new Mock<IPermissionGrantRepository>();
        _permissionGrantStoreMock = new Mock<IPermissionGrantStore>();

        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.Setup(t => t.Id).Returns("test-tenant");

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);

        var tenantProviderMock = new Mock<ITenantProvider>();

        var scopeValidator = new TenantPermissionScopeValidator(
            currentTenantMock.Object,
            currentUserMock.Object,
            tenantProviderMock.Object,
            Mock.Of<ILogger<TenantPermissionScopeValidator>>());

        _permissionGrantManager = new PermissionGrantManager(
            _permissionGrantRepositoryMock.Object,
            _permissionGrantStoreMock.Object,
            scopeValidator);
    }

    [Fact]
    public async Task GrantAsync_WhenGrantDoesNotExist_InsertsGrant()
    {
        var input = new PermissionGrantInfo
        {
            PermissionName = "Books.View",
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = "user-1",
            Scope = PermissionGrantScope.Global
        };

        _permissionGrantRepositoryMock
            .Setup(repository => repository.FindAsync(
                input.PermissionName,
                input.ProviderType,
                input.ProviderKey,
                input.Scope,
                input.TenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionGrant?)null);
        _permissionGrantRepositoryMock
            .Setup(repository => repository.InsertAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionGrant grant, CancellationToken _) => grant);

        await _permissionGrantManager.GrantAsync(input);

        _permissionGrantRepositoryMock.Verify(
            repository => repository.InsertAsync(
                It.Is<PermissionGrant>(grant =>
                    grant.PermissionName == "Books.View" &&
                    grant.ProviderType == PermissionGrantProviderType.User &&
                    grant.ProviderKey == "user-1" &&
                    grant.Scope == PermissionGrantScope.Global &&
                    grant.TenantId == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantAsync_WithTenantScopeAndMissingTenantId_ThrowsArgumentException()
    {
        var input = new PermissionGrantInfo
        {
            PermissionName = "Books.View",
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = "user-1",
            Scope = PermissionGrantScope.Tenant
        };

        var action = async () => await _permissionGrantManager.GrantAsync(input);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetEffectivePermissionsAsync_WithUserAndRoleGrants_ReturnsDistinctUnion()
    {
        _permissionGrantStoreMock
            .Setup(store => store.GetGrantedPermissionsAsync(
                PermissionGrantProviderType.User,
                "user-1",
                "tenant-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Books.View" });

        _permissionGrantStoreMock
            .Setup(store => store.GetGrantedPermissionsAsync(
                PermissionGrantProviderType.Role,
                "Librarian",
                "tenant-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Books.Edit", "Books.View" });

        var result = await _permissionGrantManager.GetEffectivePermissionsAsync(
            "user-1",
            new[] { "Librarian", "Librarian" },
            "tenant-1");

        result.Should().Equal("Books.Edit", "Books.View");
    }

    [Fact]
    public async Task RevokeAsync_CrossTenant_IsRejectedBeforeRepositoryAccess()
    {
        var (manager, repository) = CreateManager(currentTenantId: "tenant-a", isSuperAdmin: false);
        var input = TenantGrant("tenant-b");

        var action = async () => await manager.RevokeAsync(input);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*权限撤销被拒绝*");
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeAsync_GlobalScopeInTenantContext_IsRejectedBeforeRepositoryAccess()
    {
        var (manager, repository) = CreateManager(currentTenantId: "tenant-a", isSuperAdmin: false);
        var input = new PermissionGrantInfo
        {
            PermissionName = "Books.View",
            ProviderType = PermissionGrantProviderType.User,
            ProviderKey = "user-1",
            Scope = PermissionGrantScope.Global
        };

        var action = async () => await manager.RevokeAsync(input);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*权限撤销被拒绝*");
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RevokeAsync_WhenGrantDoesNotExist_IsIdempotentAndDoesNotDelete()
    {
        var (manager, repository) = CreateManager(currentTenantId: null, isSuperAdmin: false);
        repository
            .Setup(repository => repository.FindAsync(
                It.IsAny<string>(),
                It.IsAny<PermissionGrantProviderType>(),
                It.IsAny<string>(),
                It.IsAny<PermissionGrantScope>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionGrant?)null);

        await manager.RevokeAsync(TenantGrant("tenant-a"));
        await manager.RevokeAsync(TenantGrant("tenant-a"));

        repository.Verify(
            repository => repository.DeleteAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_WhenGrantExists_DeletesOnceAndSecondRevokeIsIdempotent()
    {
        var (manager, repository) = CreateManager(currentTenantId: null, isSuperAdmin: false);
        var grant = new PermissionGrant(
            Guid.NewGuid(),
            "Books.View",
            PermissionGrantProviderType.User,
            "user-1",
            PermissionGrantScope.Tenant,
            "tenant-a");
        var remaining = new List<PermissionGrant> { grant };

        repository
            .Setup(repository => repository.FindAsync(
                It.IsAny<string>(),
                It.IsAny<PermissionGrantProviderType>(),
                It.IsAny<string>(),
                It.IsAny<PermissionGrantScope>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => remaining.FirstOrDefault());
        repository
            .Setup(repository => repository.DeleteAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PermissionGrant, CancellationToken>((deleted, _) => remaining.Remove(deleted))
            .Returns(Task.CompletedTask);

        await manager.RevokeAsync(TenantGrant("tenant-a"));
        await manager.RevokeAsync(TenantGrant("tenant-a"));

        repository.Verify(
            repository => repository.DeleteAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static (PermissionGrantManager Manager, Mock<IPermissionGrantRepository> Repository) CreateManager(
        string? currentTenantId,
        bool isSuperAdmin)
    {
        var repository = new Mock<IPermissionGrantRepository>();
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(tenant => tenant.Id).Returns(currentTenantId);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsSuperAdmin).Returns(isSuperAdmin);
        var validator = new TenantPermissionScopeValidator(
            currentTenant.Object,
            currentUser.Object,
            new Mock<ITenantProvider>().Object,
            Mock.Of<ILogger<TenantPermissionScopeValidator>>());

        return (
            new PermissionGrantManager(repository.Object, new Mock<IPermissionGrantStore>().Object, validator),
            repository);
    }

    private static PermissionGrantInfo TenantGrant(string tenantId) => new()
    {
        PermissionName = "Books.View",
        ProviderType = PermissionGrantProviderType.User,
        ProviderKey = "user-1",
        Scope = PermissionGrantScope.Tenant,
        TenantId = tenantId
    };
}
