using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
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
    private readonly Mock<ICrestCacheService> _crestCacheServiceMock;
    private readonly PermissionGrantManager _permissionGrantManager;

    public PermissionGrantManagerTests()
    {
        _permissionGrantRepositoryMock = new Mock<IPermissionGrantRepository>();
        _permissionGrantStoreMock = new Mock<IPermissionGrantStore>();
        _crestCacheServiceMock = new Mock<ICrestCacheService>();

        var cacheService = new PermissionGrantCacheService(
            _crestCacheServiceMock.Object,
            new PermissionGrantCacheOptions
            {
                Expiration = TimeSpan.FromMinutes(1)
            });

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

        var cacheKeyContributor = new TenantCacheKeyContributor();

        _permissionGrantManager = new PermissionGrantManager(
            _permissionGrantRepositoryMock.Object,
            _permissionGrantStoreMock.Object,
            cacheService,
            scopeValidator,
            cacheKeyContributor,
            currentTenantMock.Object);
    }

    [Fact]
    public async Task GrantAsync_WhenGrantDoesNotExist_InsertsGrantAndInvalidatesCache()
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

        _crestCacheServiceMock.Verify(
            cache => cache.RemoveAsync(
                "Authorization.PermissionGrant",
                It.Is<object[]>(parts =>
                    parts.Length == 1 &&
                    parts[0] != null &&
                    parts[0].ToString() == "User:user-1")),
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
}
