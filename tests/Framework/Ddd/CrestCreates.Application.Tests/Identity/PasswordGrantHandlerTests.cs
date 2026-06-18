using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Security.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Identity;

public class PasswordGrantHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly Mock<IIdentitySecurityLogWriter> _securityLogWriterMock;
    private readonly Mock<ILogger<PasswordGrantHandlerImpl>> _loggerMock;
    private readonly PasswordGrantHandlerImpl _handler;

    public PasswordGrantHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _securityLogWriterMock = new Mock<IIdentitySecurityLogWriter>();
        _loggerMock = new Mock<ILogger<PasswordGrantHandlerImpl>>();
        _currentTenantMock.SetupGet(t => t.Id).Returns(string.Empty);

        _handler = new PasswordGrantHandlerImpl(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _passwordHasherMock.Object,
            _currentTenantMock.Object,
            _securityLogWriterMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_WithValidCredentials_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var user = new User(userId, "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = true
        };

        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("hashed-pw", "correct-password"))
            .Returns(true);
        _roleRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.Generic.List<Role>
            {
                new(Guid.NewGuid(), "Admin", "tenant-a")
            });

        var result = await _handler.HandleAsync("alice", "correct-password");

        result.IsSuccess.Should().BeTrue();
        result.UserId.Should().Be(userId);
        result.UserName.Should().Be("alice");
        result.Email.Should().Be("alice@test.com");
        result.TenantId.Should().Be("tenant-a");
        result.Roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task HandleAsync_WithWrongPassword_ReturnsFail()
    {
        var user = new User(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = true,
            LockoutEnabled = true
        };

        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("hashed-pw", "wrong-password"))
            .Returns(false);

        var result = await _handler.HandleAsync("alice", "wrong-password");

        result.IsSuccess.Should().BeFalse();
        result.ErrorDescription.Should().Contain("密码");
    }

    [Fact]
    public async Task HandleAsync_WithNonexistentUser_ReturnsFail()
    {
        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("nobody", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _handler.HandleAsync("nobody", "password");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithInactiveUser_ReturnsFail()
    {
        var user = new User(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = false
        };

        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync("alice", "password");

        result.IsSuccess.Should().BeFalse();
        result.ErrorDescription.Should().Contain("禁用");
    }

    [Fact]
    public async Task HandleAsync_WithLockedOutUser_ReturnsFail()
    {
        var user = new User(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = true,
            LockoutEndTime = DateTime.UtcNow.AddMinutes(10)
        };

        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync("alice", "password");

        result.IsSuccess.Should().BeFalse();
        result.ErrorDescription.Should().Contain("锁定");
    }

    [Fact]
    public async Task HandleAsync_WithTenantMismatch_ReturnsFail()
    {
        var user = new User(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = true
        };

        _currentTenantMock.SetupGet(t => t.Id).Returns("tenant-b");
        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync("alice", "password");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_AfterMaxFailedAttempts_LocksUser()
    {
        var user = new User(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = true,
            LockoutEnabled = true,
            AccessFailedCount = 4
        };

        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("hashed-pw", "wrong-password"))
            .Returns(false);
        _userRepositoryMock
            .Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync("alice", "wrong-password");

        result.IsSuccess.Should().BeFalse();
        user.AccessFailedCount.Should().Be(5);
        user.LockoutEndTime.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_WithSuccessfulLogin_ResetsFailedCount()
    {
        var userId = Guid.NewGuid();
        var user = new User(userId, "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "hashed-pw",
            IsActive = true,
            AccessFailedCount = 3,
            LockoutEndTime = DateTime.UtcNow.AddMinutes(-5)
        };

        _userRepositoryMock
            .Setup(r => r.FindByUserNameAsync("alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(h => h.VerifyPassword("hashed-pw", "correct-password"))
            .Returns(true);
        _roleRepositoryMock
            .Setup(r => r.GetByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.Generic.List<Role>());
        _userRepositoryMock
            .Setup(r => r.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync("alice", "correct-password");

        result.IsSuccess.Should().BeTrue();
        user.AccessFailedCount.Should().Be(0);
        user.LockoutEndTime.Should().BeNull();
    }
}
