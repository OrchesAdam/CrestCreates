using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantDeletionGuardTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly TenantDeletionGuard _guard;

    public TenantDeletionGuardTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();

        var options = Options.Create(new TenantDeletionOptions
        {
            RequireEmptyUsersBeforeDelete = true,
            RequireEmptyRolesBeforeDelete = true
        });

        _guard = new TenantDeletionGuard(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            options);
    }

    [Fact]
    public async Task CanDeleteAsync_WithNoUsersAndNoRoles_ReturnsSuccess()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant");

        _userRepositoryMock
            .Setup(r => r.GetListByTenantIdAsync(tenant.Id.ToString(), default))
            .ReturnsAsync(new System.Collections.Generic.List<User>());

        _roleRepositoryMock
            .Setup(r => r.GetListByTenantIdAsync(tenant.Id.ToString(), default))
            .ReturnsAsync(new System.Collections.Generic.List<Role>());

        var result = await _guard.CanDeleteAsync(tenant);

        result.CanDelete.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task CanDeleteAsync_WithUsers_ReturnsFailure()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant");

        var users = new System.Collections.Generic.List<User>
        {
            new User(Guid.NewGuid(), "user1", "user1@test.com", tenant.Id.ToString()),
            new User(Guid.NewGuid(), "user2", "user2@test.com", tenant.Id.ToString())
        };

        _userRepositoryMock
            .Setup(r => r.GetListByTenantIdAsync(tenant.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        _roleRepositoryMock
            .Setup(r => r.GetListByTenantIdAsync(tenant.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.Generic.List<Role>());

        var result = await _guard.CanDeleteAsync(tenant);

        result.CanDelete.Should().BeFalse();
        result.FailureReason.Should().Contain("2 个用户");
        result.ExistingUsers.Should().Contain("user1");
        result.ExistingUsers.Should().Contain("user2");
    }

    [Fact]
    public async Task CanDeleteAsync_WithRoles_ReturnsFailure()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant");

        _userRepositoryMock
            .Setup(r => r.GetListByTenantIdAsync(tenant.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.Generic.List<User>());

        var roles = new System.Collections.Generic.List<Role>
        {
            new Role(Guid.NewGuid(), "Admin", tenant.Id.ToString()),
            new Role(Guid.NewGuid(), "User", tenant.Id.ToString())
        };

        _roleRepositoryMock
            .Setup(r => r.GetListByTenantIdAsync(tenant.Id.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(roles);

        var result = await _guard.CanDeleteAsync(tenant);

        result.CanDelete.Should().BeFalse();
        result.FailureReason.Should().Contain("2 个角色");
        result.ExistingRoles.Should().Contain("Admin");
        result.ExistingRoles.Should().Contain("User");
    }
}
