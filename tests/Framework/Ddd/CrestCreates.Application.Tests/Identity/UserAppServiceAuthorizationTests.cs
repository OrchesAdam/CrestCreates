using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Identity;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Security.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Identity;

public class UserAppServiceAuthorizationTests
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";
    private static readonly Guid CallerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRoleRepository> _roleRepositoryMock = new();
    private readonly Mock<IUserRoleRepository> _userRoleRepositoryMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IPasswordPolicyValidator> _passwordPolicyValidatorMock = new();
    private readonly Mock<ICurrentTenant> _currentTenantMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IFeatureChecker> _featureCheckerMock = new();
    private readonly Mock<IIdentitySecurityLogWriter> _securityLogWriterMock = new();
    private readonly Mock<IPermissionChecker> _permissionCheckerMock = new();
    private readonly UserAppService _userAppService;

    public UserAppServiceAuthorizationTests()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(string.Empty);
        _currentUserMock.SetupGet(user => user.IsAuthenticated).Returns(true);
        _currentUserMock.SetupGet(user => user.Id).Returns(CallerId.ToString());
        _currentUserMock.SetupGet(user => user.IsSuperAdmin).Returns(false);
        _permissionCheckerMock
            .Setup(checker => checker.CheckAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        _featureCheckerMock
            .Setup(checker => checker.IsEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _passwordHasherMock
            .Setup(hasher => hasher.HashPassword(It.IsAny<string>()))
            .Returns("hashed-password");

        _userAppService = new UserAppService(
            _userRepositoryMock.Object,
            _roleRepositoryMock.Object,
            _userRoleRepositoryMock.Object,
            _passwordHasherMock.Object,
            _passwordPolicyValidatorMock.Object,
            _currentTenantMock.Object,
            _currentUserMock.Object,
            _featureCheckerMock.Object,
            _securityLogWriterMock.Object,
            _permissionCheckerMock.Object);
    }

    [Fact]
    public async Task Without_manage_permission_high_privilege_creation_writes_nothing()
    {
        Deny(IdentityManagementPermissions.UsersManage);

        var act = async () => await _userAppService.CreateAsync(CreateUser("alice", TenantA, isSuperAdmin: true));

        await act.Should().ThrowAsync<CrestPermissionException>();
        VerifyNoWrites();
    }

    [Fact]
    public async Task Tenant_admin_cannot_create_a_user_in_another_tenant()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);

        var act = async () => await _userAppService.CreateAsync(CreateUser("alice", TenantB));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户用户*");
        VerifyNoWrites();
    }

    [Fact]
    public async Task Host_without_the_superadmin_permission_cannot_create_a_super_admin()
    {
        Deny(IdentityManagementPermissions.UsersManageSuperAdmin);

        var act = async () => await _userAppService.CreateAsync(CreateUser("root", TenantA, isSuperAdmin: true));

        await act.Should().ThrowAsync<CrestPermissionException>();
        VerifyNoWrites();
    }

    [Fact]
    public async Task Host_with_the_superadmin_permission_creates_a_super_admin_with_audit()
    {
        SetupAvailableUserNameAndEmail();
        _userRepositoryMock
            .Setup(repository => repository.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User user, CancellationToken _) => user);

        var created = await _userAppService.CreateAsync(CreateUser("root", TenantA, isSuperAdmin: true));

        created.IsSuperAdmin.Should().BeTrue();
        _securityLogWriterMock.Verify(
            writer => writer.WriteAsync(
                created.Id, "root", TenantA, "CreateUser", true, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Tenant_context_cannot_promote_a_user_even_with_every_permission()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var user = new User(Guid.NewGuid(), "alice", "alice@test", TenantA);
        SetupUser(user);

        var act = async () => await _userAppService.UpdateAsync(user.Id, new UpdateIdentityUserDto
        {
            Email = "alice@test",
            IsSuperAdmin = true,
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*host 上下文*");
        _userRepositoryMock.Verify(
            repository => repository.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Host_cannot_demote_a_super_admin_without_the_superadmin_permission()
    {
        var user = new User(Guid.NewGuid(), "root", "root@test", TenantA) { IsSuperAdmin = true };
        SetupUser(user);
        Deny(IdentityManagementPermissions.UsersManageSuperAdmin);

        var act = async () => await _userAppService.UpdateAsync(user.Id, new UpdateIdentityUserDto
        {
            Email = "root@test",
            IsSuperAdmin = false,
        });

        await act.Should().ThrowAsync<CrestPermissionException>();
        user.IsSuperAdmin.Should().BeTrue("a rejected demotion must not mutate the loaded entity");
        _userRepositoryMock.Verify(
            repository => repository.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Tenant_admin_cannot_touch_another_tenant_user_on_any_path()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var user = new User(Guid.NewGuid(), "bob", "bob@test", TenantB);
        SetupUser(user);
        var role = new Role(Guid.NewGuid(), "Admin", TenantB);
        _roleRepositoryMock
            .Setup(repository => repository.GetAsync(role.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        var update = async () => await _userAppService.UpdateAsync(user.Id, new UpdateIdentityUserDto { Email = "bob@test" });
        var deactivate = async () => await _userAppService.SetActiveAsync(user.Id, false);
        var password = async () => await _userAppService.ChangePasswordAsync(
            user.Id, new ChangeIdentityPasswordDto { CurrentPassword = "old", NewPassword = "NewPassword1" });
        var assign = async () => await _userAppService.AssignRoleAsync(user.Id, role.Id);
        var roles = async () => await _userAppService.GetRolesAsync(user.Id);

        await update.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户用户*");
        await deactivate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户用户*");
        await password.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户用户*");
        await assign.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户用户*");
        await roles.Should().ThrowAsync<InvalidOperationException>().WithMessage("*本租户用户*");

        VerifyNoWrites();
    }

    [Fact]
    public async Task Without_manage_permission_another_users_password_and_activation_are_untouched()
    {
        Deny(IdentityManagementPermissions.UsersManage);
        var user = new User(Guid.NewGuid(), "bob", "bob@test", TenantA);

        var password = async () => await _userAppService.ChangePasswordAsync(
            user.Id, new ChangeIdentityPasswordDto { CurrentPassword = "old", NewPassword = "NewPassword1" });
        var deactivate = async () => await _userAppService.SetActiveAsync(user.Id, false);

        await password.Should().ThrowAsync<CrestPermissionException>();
        await deactivate.Should().ThrowAsync<CrestPermissionException>();
        VerifyNoWrites();
    }

    [Fact]
    public async Task Self_password_change_still_requires_the_current_password()
    {
        var user = new User(CallerId, "alice", "alice@test", TenantA) { PasswordHash = "old-hash" };
        SetupUser(user);
        _passwordHasherMock
            .Setup(hasher => hasher.VerifyPassword("old-hash", "WrongPassword1"))
            .Returns(false);

        var wrong = async () => await _userAppService.ChangePasswordAsync(
            user.Id, new ChangeIdentityPasswordDto { CurrentPassword = "WrongPassword1", NewPassword = "NewPassword1" });

        await wrong.Should().ThrowAsync<UnauthorizedAccessException>();
        _userRepositoryMock.Verify(
            repository => repository.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);

        _passwordHasherMock
            .Setup(hasher => hasher.VerifyPassword("old-hash", "OldPassword1"))
            .Returns(true);
        await _userAppService.ChangePasswordAsync(
            user.Id, new ChangeIdentityPasswordDto { CurrentPassword = "OldPassword1", NewPassword = "NewPassword1" });

        user.PasswordHash.Should().Be("hashed-password");
        _securityLogWriterMock.Verify(
            writer => writer.WriteAsync(
                user.Id, "alice", TenantA, "ChangePassword", true, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Scoped_manager_can_reset_a_password_without_the_old_one()
    {
        _currentTenantMock.SetupGet(tenant => tenant.Id).Returns(TenantA);
        var user = new User(Guid.NewGuid(), "bob", "bob@test", TenantA) { PasswordHash = "old-hash" };
        SetupUser(user);

        await _userAppService.ChangePasswordAsync(
            user.Id, new ChangeIdentityPasswordDto { CurrentPassword = "not-known", NewPassword = "NewPassword1" });

        _passwordHasherMock.Verify(
            hasher => hasher.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "a scoped manager reset does not prove the old secret");
        user.PasswordHash.Should().Be("hashed-password");
        _securityLogWriterMock.Verify(
            writer => writer.WriteAsync(
                user.Id, "bob", TenantA, "ChangePassword", true, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Without_read_permission_the_user_list_is_not_queried()
    {
        Deny(IdentityManagementPermissions.UsersRead);

        var act = async () => await _userAppService.GetListAsync();

        await act.Should().ThrowAsync<CrestPermissionException>();
        _userRepositoryMock.Verify(
            repository => repository.GetListAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _userRepositoryMock.Verify(
            repository => repository.GetListAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void Deny(string permissionName)
    {
        _permissionCheckerMock
            .Setup(checker => checker.CheckAsync(permissionName))
            .ThrowsAsync(new CrestPermissionException(permissionName));
    }

    private void SetupUser(User user)
    {
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _userRepositoryMock
            .Setup(repository => repository.UpdateAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
    }

    private void SetupAvailableUserNameAndEmail()
    {
        _userRepositoryMock
            .Setup(repository => repository.GetAsync(
                It.IsAny<Expression<Func<User, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
    }

    private void VerifyNoWrites()
    {
        _userRepositoryMock.Verify(
            repository => repository.InsertAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRepositoryMock.Verify(
            repository => repository.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRoleRepositoryMock.Verify(
            repository => repository.InsertAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _userRoleRepositoryMock.Verify(
            repository => repository.DeleteAsync(It.IsAny<UserRole>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _securityLogWriterMock.Verify(
            writer => writer.WriteAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static CreateIdentityUserDto CreateUser(string userName, string tenantId, bool isSuperAdmin = false)
        => new()
        {
            UserName = userName,
            Email = userName + "@test",
            Password = "Password1",
            TenantId = tenantId,
            IsSuperAdmin = isSuperAdmin,
        };
}
