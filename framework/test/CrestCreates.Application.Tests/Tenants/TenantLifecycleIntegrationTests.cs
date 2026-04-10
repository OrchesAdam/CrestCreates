using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Tenants;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.EventBus;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantLifecycleIntegrationTests
{
    [Fact]
    public async Task CreateTenant_ShouldAutoBootstrapAdminUserRoleAndPermissions()
    {
        var tenantRepositoryMock = new Mock<ITenantRepository>();
        var userRepositoryMock = new Mock<IUserRepository>();
        var roleRepositoryMock = new Mock<IRoleRepository>();
        var permissionGrantRepositoryMock = new Mock<IPermissionGrantRepository>();

        var services = new ServiceCollection();
        services.AddScoped(_ => userRepositoryMock.Object);
        services.AddScoped(_ => roleRepositoryMock.Object);
        services.AddScoped(_ => permissionGrantRepositoryMock.Object);
        var serviceProvider = services.BuildServiceProvider();

        var options = Options.Create(new TenantBootstrapOptions
        {
            EnableAutoBootstrap = true,
            DefaultAdminUserName = "admin",
            DefaultAdminEmail = "admin@{0}.local",
            DefaultRoleName = "Default",
            BootstrapAdminRole = true,
            BootstrapBasicPermissions = true,
            BasicPermissions = new[] { "Users.View", "Users.Create" }
        });

        var bootstrapper = new TenantBootstrapper(
            serviceProvider,
            options,
            Mock.Of<ILogger<TenantBootstrapper>>());

        var tenantBootstrapperMock = new Mock<ITenantBootstrapper>();
        tenantBootstrapperMock
            .Setup(b => b.BootstrapAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tenantManager = new TenantManager(
            tenantRepositoryMock.Object,
            tenantBootstrapperMock.Object,
            Mock.Of<ILogger<TenantManager>>());

        var tenantAppService = new TenantAppService(
            tenantManager,
            tenantRepositoryMock.Object);

        tenantRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        userRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<User>(), default))
            .ReturnsAsync((User u, CancellationToken _) => u);

        roleRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Role>(), default))
            .ReturnsAsync((Role r, CancellationToken _) => r);

        permissionGrantRepositoryMock
            .Setup(p => p.InsertAsync(It.IsAny<PermissionGrant>(), default))
            .ReturnsAsync((PermissionGrant g, CancellationToken _) => g);

        var input = new CreateTenantDto
        {
            Name = "TestTenant",
            DisplayName = "测试租户",
            DefaultConnectionString = "Server=localhost;Database=TestDb;"
        };

        var result = await tenantAppService.CreateAsync(input);

        result.Should().NotBeNull();
        result.Name.Should().Be("TestTenant");
    }

    [Fact]
    public async Task CreateTenant_WhenBootstrapFails_ShouldRollback()
    {
        var tenantRepositoryMock = new Mock<ITenantRepository>();
        var tenantBootstrapperMock = new Mock<ITenantBootstrapper>();

        var options = Options.Create(new TenantBootstrapOptions
        {
            EnableAutoBootstrap = true,
            DefaultAdminUserName = "admin",
            DefaultRoleName = "Default",
            BootstrapAdminRole = true,
            BootstrapBasicPermissions = true,
            BasicPermissions = new[] { "Test.Permission" }
        });

        var tenantManager = new TenantManager(
            tenantRepositoryMock.Object,
            tenantBootstrapperMock.Object,
            Mock.Of<ILogger<TenantManager>>());

        var tenantAppService = new TenantAppService(
            tenantManager,
            tenantRepositoryMock.Object);

        tenantRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        tenantBootstrapperMock
            .Setup(b => b.BootstrapAsync(It.IsAny<Tenant>(), default))
            .ThrowsAsync(new InvalidOperationException("数据库连接失败"));

        tenantRepositoryMock
            .Setup(r => r.DeleteAsync(It.IsAny<Tenant>(), default))
            .Returns(Task.CompletedTask);

        var input = new CreateTenantDto
        {
            Name = "FailTenant",
            DisplayName = "失败租户"
        };

        var action = async () => await tenantAppService.CreateAsync(input);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*初始化失败*已自动回滚*");

        tenantRepositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<Tenant>(), default),
            Times.Once, "初始化失败后应回滚删除租户");
    }

    [Fact]
    public async Task ArchiveAndRestoreTenant_ShouldChangeLifecycleState()
    {
        var tenantRepositoryMock = new Mock<ITenantRepository>();
        var deletionGuardMock = new Mock<ITenantDeletionGuard>();

        var options = Options.Create(new TenantDeletionOptions
        {
            Strategy = TenantDeletionStrategy.SoftDelete
        });

        var deletionManager = new TenantDeletionManager(
            tenantRepositoryMock.Object,
            deletionGuardMock.Object,
            options,
            Mock.Of<ILogger<TenantDeletionManager>>());

        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true,
            LifecycleState = TenantLifecycleState.Active
        };

        tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TestTenant", default))
            .ReturnsAsync(tenant);

        tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        deletionGuardMock
            .Setup(g => g.CanDeleteAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync(TenantDeletionGuardResult.Success());

        var archived = await deletionManager.ArchiveAsync("TestTenant");
        archived.LifecycleState.Should().Be(TenantLifecycleState.Archived);
        archived.ArchivedTime.Should().NotBeNull();

        var restored = await deletionManager.RestoreAsync("TestTenant");
        restored.LifecycleState.Should().Be(TenantLifecycleState.Active);
        restored.IsActive.Should().BeTrue();
        restored.ArchivedTime.Should().BeNull();
    }

    [Fact]
    public async Task CrossTenantPermissionGrant_ShouldBeRejected()
    {
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.Setup(t => t.Id).Returns("tenant-A");

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);

        var tenantProviderMock = new Mock<ITenantProvider>();

        var validator = new TenantPermissionScopeValidator(
            currentTenantMock.Object,
            currentUserMock.Object,
            tenantProviderMock.Object,
            Mock.Of<ILogger<TenantPermissionScopeValidator>>());

        var grant = new PermissionGrantInfo
        {
            PermissionName = "Users.Create",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Admin",
            Scope = PermissionGrantScope.Tenant,
            TenantId = "tenant-B"
        };

        var result = await validator.ValidateAsync(grant);

        result.IsAllowed.Should().BeFalse();
        result.FailureReason.Should().Contain("不属于当前租户");
    }

    [Fact]
    public async Task SuperAdminCrossTenantPermission_ShouldBeAllowed()
    {
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.Setup(t => t.Id).Returns("tenant-A");

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);

        var tenantProviderMock = new Mock<ITenantProvider>();

        var validator = new TenantPermissionScopeValidator(
            currentTenantMock.Object,
            currentUserMock.Object,
            tenantProviderMock.Object,
            Mock.Of<ILogger<TenantPermissionScopeValidator>>());

        var grant = new PermissionGrantInfo
        {
            PermissionName = "Users.Create",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Admin",
            Scope = PermissionGrantScope.Tenant,
            TenantId = "tenant-B"
        };

        var result = await validator.ValidateAsync(grant);

        result.IsAllowed.Should().BeTrue();
        result.IsSuperAdminOverride.Should().BeTrue();
    }

    [Fact]
    public void ConnectionStringDto_ShouldNotExposeRawValue()
    {
        var dtoType = typeof(TenantConnectionStringDto);
        var properties = dtoType.GetProperties();

        var hasValueProperty = properties.Any(p => p.Name == "Value");
        hasValueProperty.Should().BeFalse("DTO 不应暴露原始连接串");

        var hasMaskedValueProperty = properties.Any(p => p.Name == "MaskedValue");
        hasMaskedValueProperty.Should().BeTrue("DTO 应提供脱敏值");
    }

    [Fact]
    public void TenantCacheKey_ShouldIncludeTenantDimension()
    {
        var contributor = new TenantCacheKeyContributor();

        var key = contributor.GetTenantCacheKey("tenant-123", "Permission", "Role", "Admin");

        key.Should().Contain("Tenant");
        key.Should().Contain("tenant-123");
        key.Should().Contain("Permission:Role:Admin");
    }

    [Fact]
    public void TenantEventContext_ShouldFallbackToCurrentTenant()
    {
        var currentTenantMock = new Mock<ICurrentTenant>();
        var tenantInfo = new Mock<ITenantInfo>();
        tenantInfo.Setup(t => t.Id).Returns("tenant-123");
        tenantInfo.Setup(t => t.Name).Returns("TestTenant");
        currentTenantMock.Setup(t => t.Id).Returns("tenant-123");
        currentTenantMock.Setup(t => t.Tenant).Returns(tenantInfo.Object);

        var accessor = new TenantEventContextAccessor(currentTenantMock.Object);

        var context = accessor.TenantContext;
        context.Should().NotBeNull();
        context.TenantId.Should().Be("tenant-123");
        context.TenantName.Should().Be("TestTenant");
    }

    [Fact]
    public void AuditTenantContext_ShouldPrioritizeCurrentTenant()
    {
        var currentTenantMock = new Mock<ICurrentTenant>();
        currentTenantMock.Setup(t => t.Id).Returns("tenant-from-context");

        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.Setup(u => u.TenantId).Returns("tenant-from-user");

        var resolver = new AuditTenantContextResolver(
            currentTenantMock.Object,
            currentUserMock.Object,
            new TenantCacheKeyContributor(),
            Mock.Of<ILogger<AuditTenantContextResolver>>());

        var tenantId = resolver.ResolveTenantId();
        tenantId.Should().Be("tenant-from-context", "应优先使用 CurrentTenant 而非 CurrentUser");
    }
}
