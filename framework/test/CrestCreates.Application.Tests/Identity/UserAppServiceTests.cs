using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Identity;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Identity;

public class UserAppServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IRoleRepository> _roleRepositoryMock;
    private readonly Mock<IUserRoleRepository> _userRoleRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IPasswordPolicyValidator> _passwordPolicyValidatorMock;
    private readonly Mock<ICurrentTenant> _currentTenantMock;
    private readonly Mock<IFeatureChecker> _featureCheckerMock;
    private readonly Mock<IIdentitySecurityLogWriter> _securityLogWriterMock;
    private readonly UserAppService _userAppService;

    public UserAppServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _roleRepositoryMock = new Mock<IRoleRepository>();
        _userRoleRepositoryMock = new Mock<IUserRoleRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _passwordPolicyValidatorMock = new Mock<IPasswordPolicyValidator>();
        _currentTenantMock = new Mock<ICurrentTenant>();
        _securityLogWriterMock = new Mock<IIdentitySecurityLogWriter>();
        _currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns(string.Empty);
        _featureCheckerMock = new Mock<IFeatureChecker>();
        _featureCheckerMock
            .Setup(checker => checker.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _userAppService = new UserAppService(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _userRoleRepositoryMock.Object,
            _passwordHasherMock.Object,
            _passwordPolicyValidatorMock.Object,
            _currentTenantMock.Object,
            _featureCheckerMock.Object,
            _securityLogWriterMock.Object);
    }

    [Fact]
    public async Task CreateAsync_WithValidInput_HashesPasswordAndPersistsUser()
    {
        var input = new CreateIdentityUserDto
        {
            UserName = "alice",
            Email = "alice@test.com",
            Password = "Password1",
            TenantId = "tenant-a",
            IsSuperAdmin = true
        };

        _userRepositoryMock
            .Setup(repository => repository.GetAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _userRepositoryMock
            .Setup(repository => repository.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => user);
        _passwordHasherMock
            .Setup(hasher => hasher.HashPassword(input.Password))
            .Returns("hashed-password");

        var result = await _userAppService.CreateAsync(input);

        result.UserName.Should().Be("alice");
        result.Email.Should().Be("alice@test.com");
        result.TenantId.Should().Be("tenant-a");
        result.IsSuperAdmin.Should().BeTrue();

        _passwordPolicyValidatorMock.Verify(
            validator => validator.Validate("Password1"),
            Times.Once);
        _userRepositoryMock.Verify(
            repository => repository.InsertAsync(
                It.Is<User>(user =>
                    user.UserName == "alice" &&
                    user.Email == "alice@test.com" &&
                    user.PasswordHash == "hashed-password" &&
                    user.TenantId == "tenant-a" &&
                    user.IsSuperAdmin),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithValidCurrentPassword_ResetsLockoutState()
    {
        var userId = Guid.NewGuid();
        var user = new User(userId, "alice", "alice@test.com", "tenant-a")
        {
            PasswordHash = "old-hash",
            AccessFailedCount = 3,
            LockoutEndTime = DateTime.UtcNow.AddMinutes(10)
        };

        _userRepositoryMock
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(repository => repository.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock
            .Setup(hasher => hasher.VerifyPassword("old-hash", "OldPassword1"))
            .Returns(true);
        _passwordHasherMock
            .Setup(hasher => hasher.HashPassword("NewPassword1"))
            .Returns("new-hash");

        await _userAppService.ChangePasswordAsync(
            userId,
            new ChangeIdentityPasswordDto
            {
                CurrentPassword = "OldPassword1",
                NewPassword = "NewPassword1"
            });

        user.PasswordHash.Should().Be("new-hash");
        user.AccessFailedCount.Should().Be(0);
        user.LockoutEndTime.Should().BeNull();
        user.LastPasswordChangeTime.Should().NotBeNull();
        _passwordPolicyValidatorMock.Verify(
            validator => validator.Validate("NewPassword1"),
            Times.Once);
    }

    [Fact]
    public async Task AssignRoleAsync_WithDifferentTenant_ThrowsInvalidOperationException()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        _userRepositoryMock
            .Setup(repository => repository.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(userId, "alice", "alice@test.com", "tenant-a"));
        _roleRepositoryMock
            .Setup(repository => repository.GetAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Role(roleId, "Admin", "tenant-b"));

        var action = async () => await _userAppService.AssignRoleAsync(userId, roleId);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*不属于同一个租户*");
    }

    [Fact]
    public async Task GetListAsync_WithoutTenantId_UsesCurrentTenant()
    {
        _currentTenantMock.SetupGet(currentTenant => currentTenant.Id).Returns("tenant-a");
        _userRepositoryMock
            .Setup(repository => repository.GetListAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new System.Collections.Generic.List<User>
            {
                new(Guid.NewGuid(), "alice", "alice@test.com", "tenant-a")
            });

        var result = await _userAppService.GetListAsync();

        result.Should().ContainSingle();
        result[0].TenantId.Should().Be("tenant-a");
    }
}
