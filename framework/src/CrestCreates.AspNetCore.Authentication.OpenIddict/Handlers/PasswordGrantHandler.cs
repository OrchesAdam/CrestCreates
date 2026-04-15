using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public interface IPasswordGrantHandler
{
    Task<PasswordGrantResult> HandleAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class PasswordGrantResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorDescription { get; init; }

    // Populated on success
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? TenantId { get; init; }
    public string? OrganizationId { get; init; }
    public bool IsSuperAdmin { get; init; }
    public string[] Roles { get; init; } = Array.Empty<string>();

    public static PasswordGrantResult Fail(string description) =>
        new() { IsSuccess = false, ErrorDescription = description };

    public static PasswordGrantResult Success(
        Guid userId,
        string userName,
        string? email,
        string? tenantId,
        string? organizationId,
        bool isSuperAdmin,
        string[] roles) =>
        new()
        {
            IsSuccess = true,
            UserId = userId,
            UserName = userName,
            Email = email,
            TenantId = tenantId,
            OrganizationId = organizationId,
            IsSuperAdmin = isSuperAdmin,
            Roles = roles
        };
}

public sealed class PasswordGrantHandlerImpl : IPasswordGrantHandler
{
    // Default lockout thresholds; can be overridden by registering
    // OpenIddict-specific options once a dedicated options class is introduced.
    private const int DefaultMaxAccessFailedCount = 5;
    private const int DefaultLockoutMinutes = 15;

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentTenant _currentTenant;
    private readonly IIdentitySecurityLogWriter _securityLogService;
    private readonly ILogger<PasswordGrantHandlerImpl> _logger;

    public PasswordGrantHandlerImpl(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ICurrentTenant currentTenant,
        IIdentitySecurityLogWriter securityLogService,
        ILogger<PasswordGrantHandlerImpl> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _currentTenant = currentTenant;
        _securityLogService = securityLogService;
        _logger = logger;
    }

    public async Task<PasswordGrantResult> HandleAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedUsername = username.Trim();
        var normalizedPassword = password.Trim();
        var currentTenantId = string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id.Trim();

        var user = await _userRepository.FindByUserNameAsync(normalizedUsername, cancellationToken);
        if (user == null)
        {
            await _securityLogService.WriteAsync(
                userId: null,
                userName: normalizedUsername,
                tenantId: currentTenantId,
                action: "Login",
                isSucceeded: false,
                detail: "用户名或密码错误",
                cancellationToken: cancellationToken);
            return PasswordGrantResult.Fail("用户名或密码错误");
        }

        // 校验租户边界：user.TenantId == 请求租户上下文
        if (!string.IsNullOrWhiteSpace(currentTenantId) &&
            !string.Equals(user.TenantId, currentTenantId, StringComparison.Ordinal))
        {
            await _securityLogService.WriteAsync(
                user.Id,
                user.UserName,
                user.TenantId,
                "Login",
                false,
                "租户不匹配",
                cancellationToken);
            // Return generic message to avoid tenant enumeration
            return PasswordGrantResult.Fail("用户名或密码错误");
        }

        // 校验用户状态：IsActive
        if (!user.IsActive)
        {
            await _securityLogService.WriteAsync(
                user.Id,
                user.UserName,
                user.TenantId,
                "Login",
                false,
                "用户已被禁用",
                cancellationToken);
            return PasswordGrantResult.Fail("用户已被禁用");
        }

        // 校验用户状态：LockoutEndTime
        if (IsLockedOut(user))
        {
            await _securityLogService.WriteAsync(
                user.Id,
                user.UserName,
                user.TenantId,
                "Login",
                false,
                "用户已被锁定",
                cancellationToken);
            return PasswordGrantResult.Fail("用户已被锁定");
        }

        // 密码校验使用 IPasswordHasher
        if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !_passwordHasher.VerifyPassword(user.PasswordHash, normalizedPassword))
        {
            await HandleFailedLoginAsync(user, cancellationToken);
            return PasswordGrantResult.Fail("用户名或密码错误");
        }

        // 获取角色
        var roles = await GetRoleNamesAsync(user.Id, cancellationToken);

        // 重置失败计数并更新登录时间
        user.AccessFailedCount = 0;
        user.LockoutEndTime = null;
        user.LastLoginTime = DateTime.UtcNow;
        user.LastModificationTime = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        await _securityLogService.WriteAsync(
            user.Id,
            user.UserName,
            user.TenantId,
            "Login",
            true,
            null,
            cancellationToken);

        return PasswordGrantResult.Success(
            user.Id,
            user.UserName,
            user.Email,
            user.TenantId,
            user.OrganizationId?.ToString(),
            user.IsSuperAdmin,
            roles);
    }

    private async Task HandleFailedLoginAsync(User user, CancellationToken cancellationToken)
    {
        user.AccessFailedCount += 1;
        user.LastModificationTime = DateTime.UtcNow;

        if (user.LockoutEnabled && user.AccessFailedCount >= DefaultMaxAccessFailedCount)
        {
            user.LockoutEndTime = DateTime.UtcNow.AddMinutes(DefaultLockoutMinutes);
        }

        await _userRepository.UpdateAsync(user);

        await _securityLogService.WriteAsync(
            user.Id,
            user.UserName,
            user.TenantId,
            "Login",
            false,
            user.LockoutEndTime.HasValue && user.LockoutEndTime > DateTime.UtcNow
                ? "密码错误次数过多，用户已锁定"
                : "用户名或密码错误",
            cancellationToken);
    }

    private async Task<string[]> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.GetByUserIdAsync(userId, cancellationToken);
        return roles
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => r.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsLockedOut(User user) =>
        user.LockoutEndTime.HasValue && user.LockoutEndTime.Value > DateTime.UtcNow;
}
