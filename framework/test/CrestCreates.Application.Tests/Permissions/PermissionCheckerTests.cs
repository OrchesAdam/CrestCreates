using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using FluentAssertions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

public class PermissionCheckerTests
{
    private readonly Mock<IPermissionGrantManager> _permissionGrantManagerMock;
    private readonly Mock<ICurrentPrincipalAccessor> _currentPrincipalAccessorMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly CrestCreates.Authorization.PermissionChecker _permissionChecker;

    public PermissionCheckerTests()
    {
        _permissionGrantManagerMock = new Mock<IPermissionGrantManager>();
        _currentPrincipalAccessorMock = new Mock<ICurrentPrincipalAccessor>();
        _currentUserMock = new Mock<ICurrentUser>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns(string.Empty);

        _permissionChecker = new CrestCreates.Authorization.PermissionChecker(
            _permissionGrantManagerMock.Object,
            _currentPrincipalAccessorMock.Object,
            _currentUserMock.Object,
            _currentTenantMock.Object,
            NullLogger<CrestCreates.Authorization.PermissionChecker>.Instance);
    }

    [Fact]
    public async Task IsGrantedAsync_WhenPrincipalIsSuperAdmin_ReturnsTrue()
    {
        var principal = CreatePrincipal("user-1", new[] { "Admin" }, "tenant-1", isSuperAdmin: true);

        var result = await _permissionChecker.IsGrantedAsync(principal, "Books.Delete");

        result.Should().BeTrue();
        _permissionGrantManagerMock.Verify(
            manager => manager.GetEffectivePermissionsAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string?>(),
                It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IsGrantedAsync_WhenRolePermissionExists_ReturnsTrue()
    {
        var principal = CreatePrincipal("user-1", new[] { "Librarian" }, "tenant-1");

        _permissionGrantManagerMock
            .Setup(manager => manager.GetEffectivePermissionsAsync(
                "user-1",
                It.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "Librarian" })),
                "tenant-1",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new[] { "Books.Edit", "Books.View" });

        var result = await _permissionChecker.IsGrantedAsync(principal, "Books.Edit");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsGrantedAsync_WhenPrincipalIsAnonymous_ReturnsFalse()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _permissionChecker.IsGrantedAsync(principal, "Books.View");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsGrantedAsync_WhenCurrentTenantExists_UsesCurrentTenantInsteadOfClaimTenant()
    {
        var principal = CreatePrincipal("user-1", new[] { "Librarian" }, "tenant-from-claim");
        _currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns("tenant-from-context");

        _permissionGrantManagerMock
            .Setup(manager => manager.GetEffectivePermissionsAsync(
                "user-1",
                It.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "Librarian" })),
                "tenant-from-context",
                It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new[] { "Books.View" });

        var result = await _permissionChecker.IsGrantedAsync(principal, "Books.View");

        result.Should().BeTrue();
    }

    private static ClaimsPrincipal CreatePrincipal(
        string userId,
        string[] roles,
        string? tenantId,
        bool isSuperAdmin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "tester"),
            new("is_super_admin", isSuperAdmin.ToString().ToLowerInvariant())
        };

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            claims.Add(new Claim("tenantid", tenantId));
        }

        claims.AddRange(roles.Select(roleName => new Claim(ClaimTypes.Role, roleName)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
