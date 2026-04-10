using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Auth;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Infrastructure.Tests.Identity;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock;
    private readonly Mock<IIdentitySecurityLogRepository> _identitySecurityLogRepositoryMock;
    private readonly Mock<IPermissionGrantManager> _permissionGrantManagerMock;
    private readonly Mock<ICurrentUser> _currentUserMock;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _identitySecurityLogRepositoryMock = new Mock<IIdentitySecurityLogRepository>();
        _permissionGrantManagerMock = new Mock<IPermissionGrantManager>();
        _currentUserMock = new Mock<ICurrentUser>();
        _passwordHasher = new PasswordHasher();

        _refreshTokenRepositoryMock
            .Setup(repository => repository.InsertAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken token, CancellationToken _) => token);
        _identitySecurityLogRepositoryMock
            .Setup(repository => repository.InsertAsync(It.IsAny<IdentitySecurityLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentitySecurityLog log, CancellationToken _) => log);
        _userRepositoryMock
            .Setup(repository => repository.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => user);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        _currentUserMock.SetupGet(currentUser => currentUser.IsAuthenticated).Returns(false);

        _authService = new AuthService(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _refreshTokenRepositoryMock.Object,
            _identitySecurityLogRepositoryMock.Object,
            _permissionGrantManagerMock.Object,
            _passwordHasher,
            new IdentityClaimsBuilder(),
            _currentUserMock.Object,
            new Mock<ICurrentTenant>().Object,
            httpContextAccessor,
            Options.Create(new JwtOptions()),
            Options.Create(new IdentityAuthenticationOptions()),
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithAuthenticatedPrincipal_ReturnsCurrentUser()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        _currentUserMock.SetupGet(currentUser => currentUser.IsAuthenticated).Returns(true);
        _currentUserMock.SetupGet(currentUser => currentUser.Id).Returns(userId.ToString());
        _currentUserMock.SetupGet(currentUser => currentUser.UserName).Returns("alice");
        _currentUserMock.SetupGet(currentUser => currentUser.TenantId).Returns("tenant-a");
        _currentUserMock.SetupGet(currentUser => currentUser.OrganizationId).Returns(organizationId);
        _currentUserMock.SetupGet(currentUser => currentUser.IsSuperAdmin).Returns(true);
        _currentUserMock.SetupGet(currentUser => currentUser.Roles).Returns(["Admin"]);
        _currentUserMock.Setup(currentUser => currentUser.FindClaimValue(ClaimTypes.Email)).Returns("alice@test.com");
        _currentUserMock.Setup(currentUser => currentUser.FindClaimValue(ClaimTypes.MobilePhone)).Returns("13800000000");

        var result = await _authService.GetCurrentUserAsync();

        result.Id.Should().Be(userId);
        result.UserName.Should().Be("alice");
        result.Email.Should().Be("alice@test.com");
        result.Phone.Should().Be("13800000000");
        result.TenantId.Should().Be("tenant-a");
        result.OrganizationId.Should().Be(organizationId);
        result.IsSuperAdmin.Should().BeTrue();
        result.Roles.Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokensAndPersistsRefreshToken()
    {
        var user = CreateUser("Password1");

        _userRepositoryMock
            .Setup(repository => repository.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _roleRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>
            {
                new(Guid.NewGuid(), "Admin", user.TenantId)
                {
                    IsActive = true
                }
            });
        _permissionGrantManagerMock
            .Setup(manager => manager.GetEffectivePermissionsAsync(
                user.Id.ToString(),
                It.IsAny<IEnumerable<string>>(),
                user.TenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Books.View" });

        var result = await _authService.LoginAsync(new LoginDto
        {
            UserName = "alice",
            Password = "Password1",
            TenantId = user.TenantId
        });

        result.Token.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Token.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.User.UserName.Should().Be("alice");
        result.User.Roles.Should().ContainSingle().Which.Should().Be("Admin");
        _refreshTokenRepositoryMock.Verify(
            repository => repository.RevokeAllByUserIdAsync(user.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _refreshTokenRepositoryMock.Verify(
            repository => repository.InsertAsync(
                It.Is<RefreshToken>(token =>
                    token.UserId == user.Id &&
                    token.TenantId == user.TenantId &&
                    token.RevokedTime == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithActiveToken_RotatesRefreshToken()
    {
        var user = CreateUser("Password1");
        var currentToken = new RefreshToken(
            Guid.NewGuid(),
            user.Id,
            "refresh-token-1",
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddDays(1),
            user.TenantId);

        _refreshTokenRepositoryMock
            .Setup(repository => repository.FindByTokenAsync("refresh-token-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentToken);
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _roleRepositoryMock
            .Setup(repository => repository.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>());
        _permissionGrantManagerMock
            .Setup(manager => manager.GetEffectivePermissionsAsync(
                user.Id.ToString(),
                It.IsAny<IEnumerable<string>>(),
                user.TenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var result = await _authService.RefreshTokenAsync(new RefreshTokenDto
        {
            RefreshToken = "refresh-token-1"
        });

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBe("refresh-token-1");
        _refreshTokenRepositoryMock.Verify(
            repository => repository.RevokeAllByUserIdAsync(user.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WithExceededFailedCount_LocksUser()
    {
        var user = CreateUser("Password1");
        user.AccessFailedCount = 2;

        _userRepositoryMock
            .Setup(repository => repository.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var action = async () => await _authService.LoginAsync(new LoginDto
        {
            UserName = "alice",
            Password = "WrongPassword1",
            TenantId = user.TenantId
        });

        await action.Should().ThrowAsync<UnauthorizedAccessException>();

        user.AccessFailedCount.Should().Be(3);
        user.LockoutEndTime.Should().NotBeNull();
        user.LockoutEndTime.Should().BeAfter(DateTime.UtcNow);
    }

    private User CreateUser(string password)
    {
        return new User(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = _passwordHasher.HashPassword(password),
            IsActive = true,
            LockoutEnabled = true
        };
    }
}
