using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Security.Abstractions;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Options;
using CrestCreates.AspNetCore.Authentication.OpenIddict;
using System.Collections.Generic;
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
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<ICurrentTenant> _tenant = new();
    private readonly RefreshTokenGrantHandlerImpl _handler;

    public RefreshTokenGrantHandlerTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _securityLogWriterMock = new Mock<IIdentitySecurityLogWriter>();
        _loggerMock = new Mock<ILogger<RefreshTokenGrantHandlerImpl>>();

        _tenant.SetupGet(x => x.Id).Returns("tenant-a");
        _roles.Setup(x => x.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Role>());
        _handler = new RefreshTokenGrantHandlerImpl(
            _userRepositoryMock.Object,
            _securityLogWriterMock.Object,
            _loggerMock.Object,
            _roles.Object,
            _tenant.Object,
            new IdentityClaimsBuilder());
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
        result.Principal.Should().NotBeSameAs(principal);
        result.Principal!.GetClaim("tenant_id").Should().Be("tenant-a");
        result.Principal.GetClaim(ClaimTypes.Name).Should().Be("alice");
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

    [Theory]
    [InlineData("tenant-a", "tenant-a", "tenant-b", false)]
    [InlineData("tenant-a", "tenant-b", "tenant-a", false)]
    [InlineData("tenant-a", "tenant-a", null, false)]
    [InlineData("", null, null, true)]
    [InlineData("", null, "tenant-a", false)]
    public async Task Refresh_requires_token_user_and_current_tenant_agreement(
        string tokenTenant, string? userTenant, string? currentTenant, bool accepted)
    {
        var id = Guid.NewGuid();
        _tenant.SetupGet(x => x.Id).Returns(currentTenant);
        _userRepositoryMock.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(id, "alice", "alice@example.test", userTenant!) { IsActive = true });
        var result = await _handler.HandleAsync(CreatePrincipal(id, "alice", tokenTenant));
        result.IsSuccess.Should().Be(accepted);
        _roles.Verify(x => x.GetByUserIdAsync(id, It.IsAny<CancellationToken>()), accepted ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Refresh_replaces_all_authority_claims_and_preserves_protocol_binding()
    {
        var id = Guid.NewGuid();
        var principal = CreatePrincipal(id, "old-name", "tenant-a");
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, "old-admin"));
        identity.AddClaim(new Claim("role", "old-admin"));
        identity.AddClaim(new Claim("is_super_admin", "true"));
        identity.AddClaim(new Claim("permission", "stale.permission"));
        identity.AddClaim(new Claim("org_id", Guid.NewGuid().ToString()));
        principal.SetScopes("offline_access", "profile");
        principal.SetResources("api");
        principal.SetPresenters("client");
        principal.SetAuthorizationId("family");
        principal.SetAccessTokenLifetime(TimeSpan.FromMinutes(9));
        principal.SetRefreshTokenLifetime(TimeSpan.FromDays(2));
        principal.SetClaim(OpenIddictConstants.Claims.Private.Nonce, "protocol-nonce");
        var organization = Guid.NewGuid();
        _userRepositoryMock.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(id, "current-name", "current@example.test", "tenant-a")
            { IsActive = true, IsSuperAdmin = false, OrganizationId = organization });
        _roles.Setup(x => x.GetByUserIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Role>
        { new(Guid.NewGuid(), "reader", "tenant-a"), new(Guid.NewGuid(), "disabled", "tenant-a") { IsActive = false } });
        var result = await _handler.HandleAsync(principal);
        result.IsSuccess.Should().BeTrue();
        var fresh = result.Principal!;
        fresh.GetClaims(ClaimTypes.Role).Should().Equal("reader");
        fresh.GetClaims("role").Should().BeEmpty();
        fresh.GetClaims("permission").Should().BeEmpty();
        fresh.GetClaim("is_super_admin").Should().Be("false");
        fresh.GetClaim("org_id").Should().Be(organization.ToString());
        fresh.GetClaim(ClaimTypes.Name).Should().Be("current-name");
        fresh.GetScopes().Should().Equal(principal.GetScopes());
        fresh.GetResources().Should().Equal(principal.GetResources());
        fresh.GetPresenters().Should().Equal(principal.GetPresenters());
        fresh.GetAuthorizationId().Should().Be("family");
        fresh.GetAccessTokenLifetime().Should().Be(TimeSpan.FromMinutes(9));
        fresh.GetRefreshTokenLifetime().Should().Be(TimeSpan.FromDays(2));
        fresh.GetClaim(OpenIddictConstants.Claims.Private.Nonce).Should().Be("protocol-nonce");
    }

    [Theory]
    [InlineData("tenantid", "tenant-b")]
    [InlineData("tenant_id", "tenant-a")]
    public async Task Conflicting_or_duplicate_tenant_claims_fail_before_role_read(string claimType, string tenantValue)
    {
        var id = Guid.NewGuid();
        _userRepositoryMock.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(id, "alice", "alice@example.test", "tenant-a") { IsActive = true });
        var principal = CreatePrincipal(id, "alice", "tenant-a");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(claimType, tenantValue));
        (await _handler.HandleAsync(principal)).IsSuccess.Should().BeFalse();
        _roles.Verify(x => x.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
