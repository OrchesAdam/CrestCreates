using System;
using System.Threading.Tasks;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

public class TenantPermissionScopeValidatorTests
{
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<ITenantProvider> _tenantProviderMock;
    private readonly TenantPermissionScopeValidator _validator;

    public TenantPermissionScopeValidatorTests()
    {
        _currentTenantMock = new Mock<ICurrentTenant>();
        _currentUserMock = new Mock<ICurrentUser>();
        _tenantProviderMock = new Mock<ITenantProvider>();

        _validator = new TenantPermissionScopeValidator(
            _currentTenantMock.Object,
            _currentUserMock.Object,
            _tenantProviderMock.Object,
            Mock.Of<ILogger<TenantPermissionScopeValidator>>());
    }

    [Fact]
    public async Task ValidateAsync_WithGlobalScope_AlwaysAllows()
    {
        var grant = new PermissionGrantInfo
        {
            PermissionName = "Test.Permission",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Admin",
            Scope = PermissionGrantScope.Global
        };

        var result = await _validator.ValidateAsync(grant);

        result.IsAllowed.Should().BeTrue();
        result.IsSuperAdminOverride.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WithSuperAdmin_CrossTenantAlwaysAllowed()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);

        var grant = new PermissionGrantInfo
        {
            PermissionName = "Test.Permission",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Admin",
            Scope = PermissionGrantScope.Tenant,
            TenantId = "different-tenant"
        };

        var result = await _validator.ValidateAsync(grant);

        result.IsAllowed.Should().BeTrue();
        result.IsSuperAdminOverride.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithMatchingTenantId_Allows()
    {
        var tenantId = "test-tenant-id";
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentTenantMock.Setup(t => t.Id).Returns(tenantId);

        var grant = new PermissionGrantInfo
        {
            PermissionName = "Test.Permission",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Admin",
            Scope = PermissionGrantScope.Tenant,
            TenantId = tenantId
        };

        var result = await _validator.ValidateAsync(grant);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WithDifferentTenantId_Denies()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentTenantMock.Setup(t => t.Id).Returns("current-tenant");

        var grant = new PermissionGrantInfo
        {
            PermissionName = "Test.Permission",
            ProviderType = PermissionGrantProviderType.Role,
            ProviderKey = "Admin",
            Scope = PermissionGrantScope.Tenant,
            TenantId = "different-tenant"
        };

        var result = await _validator.ValidateAsync(grant);

        result.IsAllowed.Should().BeFalse();
        result.FailureReason.Should().Contain("不属于当前租户");
    }

    [Fact]
    public async Task CanGrantPermissionToTenantAsync_WithSuperAdmin_AlwaysTrue()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(true);

        var result = await _validator.CanGrantPermissionToTenantAsync("any-tenant");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanGrantPermissionToTenantAsync_WithMatchingTenant_True()
    {
        var tenantId = "test-tenant";
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentTenantMock.Setup(t => t.Id).Returns(tenantId);

        var result = await _validator.CanGrantPermissionToTenantAsync(tenantId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanGrantPermissionToTenantAsync_WithDifferentTenant_False()
    {
        _currentUserMock.Setup(u => u.IsSuperAdmin).Returns(false);
        _currentTenantMock.Setup(t => t.Id).Returns("current-tenant");

        var result = await _validator.CanGrantPermissionToTenantAsync("different-tenant");

        result.Should().BeFalse();
    }
}
