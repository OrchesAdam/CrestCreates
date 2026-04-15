using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Domain.Authorization;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public interface IRefreshTokenGrantHandler
{
    Task<RefreshTokenGrantResult> HandleAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

public sealed class RefreshTokenGrantResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorDescription { get; init; }
    public ClaimsPrincipal? Principal { get; init; }

    public static RefreshTokenGrantResult Fail(string description) =>
        new() { IsSuccess = false, ErrorDescription = description };

    public static RefreshTokenGrantResult Success(ClaimsPrincipal principal) =>
        new() { IsSuccess = true, Principal = principal };
}

public sealed class RefreshTokenGrantHandlerImpl : IRefreshTokenGrantHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentitySecurityLogWriter _securityLogService;
    private readonly ILogger<RefreshTokenGrantHandlerImpl> _logger;

    public RefreshTokenGrantHandlerImpl(
        IUserRepository userRepository,
        IIdentitySecurityLogWriter securityLogService,
        ILogger<RefreshTokenGrantHandlerImpl> logger)
    {
        _userRepository = userRepository;
        _securityLogService = securityLogService;
        _logger = logger;
    }

    public async Task<RefreshTokenGrantResult> HandleAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        // 从令牌中提取用户 ID
        var subjectClaim = principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (string.IsNullOrWhiteSpace(subjectClaim) || !Guid.TryParse(subjectClaim, out var userId))
        {
            _logger.LogWarning("RefreshToken grant: subject claim missing or invalid.");
            return RefreshTokenGrantResult.Fail("刷新令牌无效");
        }

        var tenantId = principal.GetClaim("tenant_id");
        var userName = principal.GetClaim(OpenIddictConstants.Claims.Name);

        // 用户状态验证
        var user = await _userRepository.GetAsync(userId, cancellationToken);
        if (user == null)
        {
            await _securityLogService.WriteAsync(
                userId: userId,
                userName: userName,
                tenantId: tenantId,
                action: "RefreshToken",
                isSucceeded: false,
                detail: "用户不存在",
                cancellationToken: cancellationToken);
            return RefreshTokenGrantResult.Fail("刷新令牌无效");
        }

        if (!user.IsActive)
        {
            await _securityLogService.WriteAsync(
                userId: userId,
                userName: user.UserName,
                tenantId: user.TenantId,
                action: "RefreshToken",
                isSucceeded: false,
                detail: "用户已被禁用",
                cancellationToken: cancellationToken);
            return RefreshTokenGrantResult.Fail("用户已被禁用");
        }

        if (user.LockoutEndTime.HasValue && user.LockoutEndTime.Value > DateTime.UtcNow)
        {
            await _securityLogService.WriteAsync(
                userId: userId,
                userName: user.UserName,
                tenantId: user.TenantId,
                action: "RefreshToken",
                isSucceeded: false,
                detail: "用户已被锁定",
                cancellationToken: cancellationToken);
            return RefreshTokenGrantResult.Fail("用户已被锁定");
        }

        // 记录安全日志
        await _securityLogService.WriteAsync(
            userId: userId,
            userName: user.UserName,
            tenantId: user.TenantId,
            action: "RefreshToken",
            isSucceeded: true,
            cancellationToken: cancellationToken);

        // 返回原有 ClaimsPrincipal（保持原有 claims）
        return RefreshTokenGrantResult.Success(principal);
    }
}
