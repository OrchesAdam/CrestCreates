using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Identity;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Application.Identity;

[CrestService]
public class UserAppService : IUserAppService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPasswordPolicyValidator _passwordPolicyValidator;
    private readonly ICurrentTenant _currentTenant;
    private readonly IFeatureChecker _featureChecker;

    public UserAppService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IUserRoleRepository userRoleRepository,
        IPasswordHasher passwordHasher,
        IPasswordPolicyValidator passwordPolicyValidator,
        ICurrentTenant currentTenant,
        IFeatureChecker featureChecker)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _passwordHasher = passwordHasher;
        _passwordPolicyValidator = passwordPolicyValidator;
        _currentTenant = currentTenant;
        _featureChecker = featureChecker;
    }

    public async Task<IdentityUserDto> CreateAsync(
        CreateIdentityUserDto input,
        CancellationToken cancellationToken = default)
    {
        if (!await _featureChecker.IsEnabledAsync("Identity.UserCreationEnabled", cancellationToken))
        {
            throw new InvalidOperationException("用户创建功能已禁用");
        }

        var userName = NormalizeRequired(input.UserName, nameof(input.UserName));
        var email = NormalizeRequired(input.Email, nameof(input.Email));
        var tenantId = NormalizeRequired(input.TenantId, nameof(input.TenantId));

        await EnsureUserNameAvailableAsync(userName, tenantId, cancellationToken);
        await EnsureEmailAvailableAsync(email, tenantId, null, cancellationToken);
        _passwordPolicyValidator.Validate(input.Password);

        var user = new User(Guid.NewGuid(), userName, email, tenantId)
        {
            PasswordHash = _passwordHasher.HashPassword(input.Password),
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            OrganizationId = input.OrganizationId,
            IsActive = true,
            IsSuperAdmin = input.IsSuperAdmin,
            CreationTime = DateTime.UtcNow,
            LastPasswordChangeTime = DateTime.UtcNow
        };

        await _userRepository.InsertAsync(user, cancellationToken);
        return MapToDto(user);
    }

    public async Task<IdentityUserDto?> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(userId, cancellationToken);
        return user == null ? null : MapToDto(user);
    }

    public async Task<IReadOnlyList<IdentityUserDto>> GetListAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = string.IsNullOrWhiteSpace(tenantId) ? _currentTenant.Id : tenantId;
        var users = string.IsNullOrWhiteSpace(effectiveTenantId)
            ? await _userRepository.GetListAsync(cancellationToken)
            : await _userRepository.GetListAsync(user => user.TenantId == effectiveTenantId, cancellationToken);

        return users
            .OrderBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(MapToDto)
            .ToArray();
    }

    public async Task<IdentityUserDto> UpdateAsync(
        Guid userId,
        UpdateIdentityUserDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(userId, cancellationToken)
                   ?? throw new InvalidOperationException($"用户 '{userId}' 不存在");

        var email = NormalizeRequired(input.Email, nameof(input.Email));
        await EnsureEmailAvailableAsync(email, user.TenantId, user.Id, cancellationToken);

        user.Email = email;
        user.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        user.OrganizationId = input.OrganizationId;
        user.IsSuperAdmin = input.IsSuperAdmin;
        user.LastModificationTime = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
        return MapToDto(user);
    }

    public async Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(userId, cancellationToken)
                   ?? throw new InvalidOperationException($"用户 '{userId}' 不存在");

        user.IsActive = isActive;
        user.LastModificationTime = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangeIdentityPasswordDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(userId, cancellationToken)
                   ?? throw new InvalidOperationException($"用户 '{userId}' 不存在");

        if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !_passwordHasher.VerifyPassword(user.PasswordHash, input.CurrentPassword))
        {
            throw new UnauthorizedAccessException("当前密码错误");
        }

        _passwordPolicyValidator.Validate(input.NewPassword);
        user.PasswordHash = _passwordHasher.HashPassword(input.NewPassword);
        user.AccessFailedCount = 0;
        user.LockoutEndTime = null;
        user.LastPasswordChangeTime = DateTime.UtcNow;
        user.LastModificationTime = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);
    }

    public async Task AssignRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetAsync(userId, cancellationToken)
                   ?? throw new InvalidOperationException($"用户 '{userId}' 不存在");
        var role = await _roleRepository.GetAsync(roleId, cancellationToken)
                   ?? throw new InvalidOperationException($"角色 '{roleId}' 不存在");

        if (!string.Equals(user.TenantId, role.TenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("用户和角色不属于同一个租户");
        }

        var existingLink = await _userRoleRepository.FindAsync(userId, roleId, cancellationToken);
        if (existingLink != null)
        {
            return;
        }

        await _userRoleRepository.InsertAsync(
            new UserRole(Guid.NewGuid(), userId, roleId, user.TenantId),
            cancellationToken);
    }

    public async Task RemoveRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default)
    {
        var existingLink = await _userRoleRepository.FindAsync(userId, roleId, cancellationToken);
        if (existingLink == null)
        {
            return;
        }

        await _userRoleRepository.DeleteAsync(existingLink, cancellationToken);
    }

    public async Task<IReadOnlyList<IdentityUserRoleDto>> GetRolesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var roles = await _roleRepository.GetByUserIdAsync(userId, cancellationToken);
        return roles
            .Select(role => new IdentityUserRoleDto
            {
                RoleId = role.Id,
                RoleName = role.Name,
                DisplayName = role.DisplayName
            })
            .ToArray();
    }

    private async Task EnsureUserNameAvailableAsync(
        string userName,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var existingUser = await _userRepository.GetAsync(
            user => user.UserName == userName && user.TenantId == tenantId,
            cancellationToken);

        if (existingUser != null)
        {
            throw new InvalidOperationException($"用户名 '{userName}' 已存在");
        }
    }

    private async Task EnsureEmailAvailableAsync(
        string email,
        string tenantId,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var existingUser = await _userRepository.GetAsync(
            user => user.Email == email && user.TenantId == tenantId,
            cancellationToken);

        if (existingUser != null && (!currentUserId.HasValue || existingUser.Id != currentUserId.Value))
        {
            throw new InvalidOperationException($"邮箱 '{email}' 已存在");
        }
    }

    private static IdentityUserDto MapToDto(User user)
    {
        return new IdentityUserDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            Phone = user.Phone,
            TenantId = user.TenantId,
            OrganizationId = user.OrganizationId,
            IsActive = user.IsActive,
            IsSuperAdmin = user.IsSuperAdmin,
            AccessFailedCount = user.AccessFailedCount,
            IsLockedOut = user.LockoutEndTime.HasValue && user.LockoutEndTime.Value > DateTime.UtcNow,
            LockoutEndTime = user.LockoutEndTime
        };
    }

    private static string NormalizeRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("参数不能为空", parameterName);
        }

        return value.Trim();
    }
}
