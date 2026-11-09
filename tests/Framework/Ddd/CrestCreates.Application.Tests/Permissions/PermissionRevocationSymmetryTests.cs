using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

public class PermissionRevocationSymmetryTests
{
    [Fact]
    public async Task Revoke_ShouldBeObservedByPermissionCheckerImmediately()
    {
        var grants = new List<PermissionGrant>();
        var repository = new Mock<IPermissionGrantRepository>();
        repository
            .Setup(repo => repo.GetListByProviderAsync(
                It.IsAny<PermissionGrantProviderType>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => grants.ToList());
        repository
            .Setup(repo => repo.FindAsync(
                It.IsAny<string>(),
                It.IsAny<PermissionGrantProviderType>(),
                It.IsAny<string>(),
                It.IsAny<PermissionGrantScope>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string permissionName, PermissionGrantProviderType providerType, string providerKey,
                PermissionGrantScope scope, string? tenantId, CancellationToken _) =>
                grants.FirstOrDefault(grant =>
                    grant.PermissionName == permissionName &&
                    grant.ProviderType == providerType &&
                    grant.ProviderKey == providerKey &&
                    grant.Scope == scope));
        repository
            .Setup(repo => repo.InsertAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PermissionGrant, CancellationToken>((grant, _) => grants.Add(grant))
            .ReturnsAsync((PermissionGrant grant, CancellationToken _) => grant);
        repository
            .Setup(repo => repo.DeleteAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()))
            .Callback<PermissionGrant, CancellationToken>((grant, _) => grants.Remove(grant))
            .Returns(Task.CompletedTask);

        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(currentTenant => currentTenant.Id).Returns(string.Empty);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsSuperAdmin).Returns(false);
        var validator = new TenantPermissionScopeValidator(
            tenant.Object,
            currentUser.Object,
            new Mock<ITenantProvider>().Object,
            Mock.Of<ILogger<TenantPermissionScopeValidator>>());

        var manager = new PermissionGrantManager(repository.Object, new PermissionGrantStore(repository.Object), validator);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Role, "Librarian")
        }, "revocation-authority-test"));
        var accessor = new Mock<ICurrentPrincipalAccessor>();
        accessor.SetupGet(value => value.Principal).Returns(principal);

        var checker = new PermissionChecker(
            manager,
            accessor.Object,
            currentUser.Object,
            tenant.Object,
            NullLogger<PermissionChecker>.Instance);

        var grant = new PermissionGrantInfo
        {
            PermissionName = "Books.View",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Librarian",
            Scope = PermissionGrantScope.Global
        };

        await manager.GrantAsync(grant);
        (await checker.IsGrantedAsync("Books.View")).Should().BeTrue();

        await manager.RevokeAsync(grant);
        (await checker.IsGrantedAsync("Books.View")).Should().BeFalse();

        await manager.RevokeAsync(grant);
        grants.Should().BeEmpty();
    }
}
