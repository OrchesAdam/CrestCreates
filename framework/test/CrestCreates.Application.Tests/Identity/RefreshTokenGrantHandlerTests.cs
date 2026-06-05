using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Security.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using Xunit;

namespace CrestCreates.Application.Tests.Identity;

public class RefreshTokenGrantHandlerTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IIdentitySecurityLogWriter> _securityLogWriterMock;
    private readonly Mock<ILogger<RefreshTokenGrantHandlerImpl>> _loggerMock;
    private readonly RefreshTokenGrantHandlerImpl _handler;

    public RefreshTokenGrantHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _securityLogWriterMock = new Mock<IIdentitySecurityLogWriter>();
        _loggerMock = new Mock<ILogger<RefreshTokenGrantHandlerImpl>>();

        _handler = new RefreshTokenGrantHandlerImpl(
            _userRepositoryMock.Object,
            _securityLogWriterMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task HandleAsync_WithValidPrincipal_ReturnsSuccess()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, "alice", "tenant-a");
        var user = new User(userId, "alice", "alice@test.com", "tenant-a") { IsActive = true };

        _userRepositoryMock
            .Setup(r => r.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync(principal);

        result.IsSuccess.Should().BeTrue();
        result.Principal.Should().BeSameAs(principal);
    }

    [Fact]
    public async Task HandleAsync_WithMissingSubject_ReturnsFail()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _handler.HandleAsync(principal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorDescription.Should().Contain("无效");
    }

    [Fact]
    public async Task HandleAsync_WithInvalidSubjectGuid_ReturnsFail()
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, "not-a-guid"));
        var principal = new ClaimsPrincipal(identity);

        var result = await _handler.HandleAsync(principal);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithDeletedUser_ReturnsFail()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, "alice", "tenant-a");

        _userRepositoryMock
            .Setup(r => r.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _handler.HandleAsync(principal);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithInactiveUser_ReturnsFail()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, "alice", "tenant-a");
        var user = new User(userId, "alice", "alice@test.com", "tenant-a") { IsActive = false };

        _userRepositoryMock
            .Setup(r => r.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync(principal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorDescription.Should().Contain("禁用");
    }

    [Fact]
    public async Task HandleAsync_WithLockedOutUser_ReturnsFail()
    {
        var userId = Guid.NewGuid();
        var principal = CreatePrincipal(userId, "alice", "tenant-a");
        var user = new User(userId, "alice", "alice@test.com", "tenant-a")
        {
            IsActive = true,
            LockoutEndTime = DateTime.UtcNow.AddMinutes(10)
        };

        _userRepositoryMock
            .Setup(r => r.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.HandleAsync(principal);

        result.IsSuccess.Should().BeFalse();
        result.ErrorDescription.Should().Contain("锁定");
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, string userName, string tenantId)
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, userId.ToString()));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, userName));
        identity.AddClaim(new Claim("tenant_id", tenantId));
        return new ClaimsPrincipal(identity);
    }
}
