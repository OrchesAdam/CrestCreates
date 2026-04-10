using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantDeletionManagerTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly Mock<ITenantDeletionGuard> _deletionGuardMock;
    private readonly TenantDeletionManager _deletionManager;

    public TenantDeletionManagerTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _deletionGuardMock = new Mock<ITenantDeletionGuard>();

        var options = Options.Create(new TenantDeletionOptions
        {
            Strategy = TenantDeletionStrategy.SoftDelete,
            RequireEmptyUsersBeforeDelete = true,
            RequireEmptyRolesBeforeDelete = true
        });

        _deletionManager = new TenantDeletionManager(
            _tenantRepositoryMock.Object,
            _deletionGuardMock.Object,
            options,
            Mock.Of<ILogger<TenantDeletionManager>>());
    }

    [Fact]
    public async Task ArchiveAsync_WithValidTenant_SetsLifecycleStateToArchived()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active
        };

        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TestTenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        var result = await _deletionManager.ArchiveAsync("TestTenant");

        result.LifecycleState.Should().Be(TenantLifecycleState.Archived);
        result.ArchivedTime.Should().NotBeNull();
    }

    [Fact]
    public async Task RestoreAsync_WithArchivedTenant_SetsLifecycleStateToActive()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = false,
            LifecycleState = TenantLifecycleState.Archived,
            ArchivedTime = DateTime.UtcNow
        };

        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TestTenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        var result = await _deletionManager.RestoreAsync("TestTenant");

        result.LifecycleState.Should().Be(TenantLifecycleState.Active);
        result.ArchivedTime.Should().BeNull();
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteAsync_WithTenantHavingUsers_ThrowsException()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active
        };

        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TestTenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _deletionGuardMock
            .Setup(g => g.CanDeleteAsync(tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantDeletionGuardResult.Failure("租户下仍有 5 个用户"));

        var action = async () => await _deletionManager.SoftDeleteAsync("TestTenant");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*租户下仍有 5 个用户*");
    }

    [Fact]
    public async Task DeleteAsync_WithForbiddenStrategy_ThrowsException()
    {
        var forbiddenOptions = Options.Create(new TenantDeletionOptions
        {
            Strategy = TenantDeletionStrategy.Forbidden
        });

        var forbiddenManager = new TenantDeletionManager(
            _tenantRepositoryMock.Object,
            _deletionGuardMock.Object,
            forbiddenOptions,
            Mock.Of<ILogger<TenantDeletionManager>>());

        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true
        };

        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TestTenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var action = async () => await forbiddenManager.DeleteAsync("TestTenant");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*删除已被禁用*");
    }

    [Fact]
    public async Task DeleteAsync_WithArchiveStrategy_ArchivesTenant()
    {
        var archiveOptions = Options.Create(new TenantDeletionOptions
        {
            Strategy = TenantDeletionStrategy.Archive
        });

        var archiveManager = new TenantDeletionManager(
            _tenantRepositoryMock.Object,
            _deletionGuardMock.Object,
            archiveOptions,
            Mock.Of<ILogger<TenantDeletionManager>>());

        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active
        };

        _tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TestTenant", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        await archiveManager.DeleteAsync("TestTenant");

        tenant.LifecycleState.Should().Be(TenantLifecycleState.Archived);
    }
}
