using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Tenants;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantBootstrapperTests
{
    private readonly Mock<ITenantRepository> _tenantRepositoryMock;
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPermissionGrantRepository> _permissionGrantRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly IServiceProvider _serviceProvider;
    private readonly TenantBootstrapper _bootstrapper;

    public TenantBootstrapperTests()
    {
        _tenantRepositoryMock = new Mock<ITenantRepository>();
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _permissionGrantRepositoryMock = new Mock<IPermissionGrantRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _passwordHasherMock
            .Setup(p => p.HashPassword(It.IsAny<string>()))
            .Returns("hashed-password-123");

        var services = new ServiceCollection();
        services.AddScoped(_ => _userRepositoryMock.Object);
        services.AddScoped(_ => _roleRepositoryMock.Object);
        services.AddScoped(_ => _permissionGrantRepositoryMock.Object);
        services.AddScoped(_ => _passwordHasherMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        var options = Options.Create(new TenantBootstrapOptions
        {
            EnableAutoBootstrap = true,
            BootstrapAdminRole = true,
            BootstrapBasicPermissions = true,
            BasicPermissions = new[] { "Test.Permission1", "Test.Permission2" }
        });

        var loggerMock = new Mock<ILogger<TenantBootstrapper>>();

        _bootstrapper = new TenantBootstrapper(
            _serviceProvider,
            options,
            loggerMock.Object);
    }

    [Fact]
    public async Task BootstrapAsync_WithValidTenant_CreatesDefaultRole()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true
        };

        _userRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => user);

        _roleRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role role, CancellationToken _) => role);

        _permissionGrantRepositoryMock
            .Setup(p => p.InsertAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionGrant grant, CancellationToken _) => grant);

        await _bootstrapper.BootstrapAsync(tenant);

        _userRepositoryMock.Verify(
            r => r.InsertAsync(
                It.Is<User>(u =>
                    u.UserName == "admin" &&
                    u.IsSuperAdmin &&
                    u.TenantId == tenant.Id.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _roleRepositoryMock.Verify(
            r => r.InsertAsync(
                It.Is<Role>(role =>
                    role.Name == "Default" &&
                    role.TenantId == tenant.Id.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BootstrapAsync_WithValidTenant_GrantsBasicPermissions()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true
        };

        _userRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => user);

        _roleRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Role role, CancellationToken _) => role);

        _permissionGrantRepositoryMock
            .Setup(p => p.InsertAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionGrant grant, CancellationToken _) => grant);

        await _bootstrapper.BootstrapAsync(tenant);

        _permissionGrantRepositoryMock.Verify(
            p => p.InsertAsync(
                It.Is<PermissionGrant>(grant =>
                    grant.ProviderType == PermissionGrantProviderType.Role &&
                    grant.ProviderKey == "Default" &&
                    grant.Scope == PermissionGrantScope.Tenant &&
                    grant.TenantId == tenant.Id.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task BootstrapAsync_WhenDisabled_DoesNotCreateRoleOrPermissions()
    {
        var tenant = new Tenant(Guid.NewGuid(), "TestTenant")
        {
            IsActive = true
        };

        var disabledOptions = Options.Create(new TenantBootstrapOptions
        {
            EnableAutoBootstrap = false
        });

        var disabledBootstrapper = new TenantBootstrapper(
            _serviceProvider,
            disabledOptions,
            Mock.Of<ILogger<TenantBootstrapper>>());

        await disabledBootstrapper.BootstrapAsync(tenant);

        _roleRepositoryMock.Verify(
            r => r.InsertAsync(It.IsAny<Role>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _permissionGrantRepositoryMock.Verify(
            p => p.InsertAsync(It.IsAny<PermissionGrant>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
