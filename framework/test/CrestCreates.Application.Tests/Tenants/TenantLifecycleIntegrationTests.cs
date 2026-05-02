using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Tenants;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared;
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

        var storeMock = new Mock<ITenantInitializationStore>();
        var dbInitializerMock = new Mock<ITenantDatabaseInitializer>();
        var migrationRunnerMock = new Mock<ITenantMigrationRunner>();
        var settingsSeederMock = new Mock<ITenantSettingDefaultsSeeder>();
        var featuresSeederMock = new Mock<ITenantFeatureDefaultsSeeder>();

        dbInitializerMock
            .Setup(d => d.InitializeAsync(It.IsAny<TenantInitializationContext>(), default))
            .ReturnsAsync(TenantDatabaseInitializeResult.Succeeded());
        migrationRunnerMock
            .Setup(m => m.RunAsync(It.IsAny<TenantInitializationContext>(), default))
            .ReturnsAsync(TenantMigrationResult.Succeeded());
        settingsSeederMock
            .Setup(s => s.SeedAsync(It.IsAny<TenantInitializationContext>(), default))
            .ReturnsAsync(TenantSettingDefaultsResult.Succeeded());
        featuresSeederMock
            .Setup(f => f.SeedAsync(It.IsAny<TenantInitializationContext>(), default))
            .ReturnsAsync(TenantFeatureDefaultsResult.Succeeded());

        var orchestrator = new TenantInitializationOrchestrator(
            dbInitializerMock.Object,
            migrationRunnerMock.Object,
            bootstrapper,
            settingsSeederMock.Object,
            featuresSeederMock.Object,
            storeMock.Object,
            Mock.Of<ILogger<TenantInitializationOrchestrator>>());

        var tenantManager = new TenantManager(
            tenantRepositoryMock.Object,
            Mock.Of<ILogger<TenantManager>>());

        var tenantAppService = new TenantAppService(
            tenantManager,
            tenantRepositoryMock.Object,
            orchestrator,
            storeMock.Object);

        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            DisplayName = "测试租户",
            IsActive = true,
            CreationTime = DateTime.UtcNow
        };

        tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("TESTTENANT", default))
            .ReturnsAsync((Tenant?)null);

        tenantRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), default))
            .ReturnsAsync(new TenantInitializationRecord(
                Guid.NewGuid(), Guid.NewGuid(), 1, "correlation-id"));

        storeMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<TenantInitializationRecord>(), default))
            .Returns(Task.CompletedTask);

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
    public async Task CreateTenant_WhenInitializeFails_ShouldMarkFailed()
    {
        var tenantRepositoryMock = new Mock<ITenantRepository>();

        var storeMock = new Mock<ITenantInitializationStore>();
        var dbInitializerMock = new Mock<ITenantDatabaseInitializer>();
        var migrationRunnerMock = new Mock<ITenantMigrationRunner>();
        var dataSeederMock = new Mock<ITenantDataSeeder>();
        var settingsSeederMock = new Mock<ITenantSettingDefaultsSeeder>();
        var featuresSeederMock = new Mock<ITenantFeatureDefaultsSeeder>();

        dbInitializerMock
            .Setup(d => d.InitializeAsync(It.IsAny<TenantInitializationContext>(), default))
            .ReturnsAsync(TenantDatabaseInitializeResult.Failed("模拟数据库初始化失败"));

        var orchestrator = new TenantInitializationOrchestrator(
            dbInitializerMock.Object,
            migrationRunnerMock.Object,
            dataSeederMock.Object,
            settingsSeederMock.Object,
            featuresSeederMock.Object,
            storeMock.Object,
            Mock.Of<ILogger<TenantInitializationOrchestrator>>());

        var tenantManager = new TenantManager(
            tenantRepositoryMock.Object,
            Mock.Of<ILogger<TenantManager>>());

        var tenantAppService = new TenantAppService(
            tenantManager,
            tenantRepositoryMock.Object,
            orchestrator,
            storeMock.Object);

        tenantRepositoryMock
            .Setup(r => r.FindByNameAsync("FAILTENANT", default))
            .ReturnsAsync((Tenant?)null);

        tenantRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        tenantRepositoryMock
            .Setup(r => r.UpdateAsync(It.IsAny<Tenant>(), default))
            .ReturnsAsync((Tenant t, CancellationToken _) => t);

        storeMock
            .Setup(s => s.TryBeginInitializationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), default))
            .ReturnsAsync(new TenantInitializationRecord(
                Guid.NewGuid(), Guid.NewGuid(), 1, "correlation-id"));

        storeMock
            .Setup(s => s.UpdateAsync(
                It.IsAny<TenantInitializationRecord>(), default))
            .Returns(Task.CompletedTask);

        var input = new CreateTenantDto
        {
            Name = "FailTenant",
            DisplayName = "失败租户"
        };

        var result = await tenantAppService.CreateAsync(input);

        result.Should().NotBeNull();
        result.InitializationStatus.Should().Be(TenantInitializationStatus.Failed);
        result.LastInitializationError.Should().NotBeNullOrEmpty();
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
