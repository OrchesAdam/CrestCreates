using System;
using System.Security.Claims;
using System.Linq;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.MultiTenancy.Abstract;
using OpenIddict.Server.AspNetCore;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AspNetCore.Authentication.OpenIddict.Services;
using CrestCreates.Security.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace CrestCreates.AspNetCore.Authentication.OpenIddict.Handlers;

public sealed class RefreshTokenGrantHandlerImpl : IRefreshTokenGrantHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentitySecurityLogWriter _securityLogService;
    private readonly ILogger<RefreshTokenGrantHandlerImpl> _logger;
    private readonly IRoleRepository _roleRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IIdentityClaimsBuilder _claimsBuilder;

    public RefreshTokenGrantHandlerImpl(
        IUserRepository userRepository,
        IIdentitySecurityLogWriter securityLogService,
        ILogger<RefreshTokenGrantHandlerImpl> logger,
        IRoleRepository roleRepository,
        ICurrentTenant currentTenant,
        IIdentityClaimsBuilder claimsBuilder)
    {
        _userRepository = userRepository;
        _securityLogService = securityLogService;
        _logger = logger;
        _roleRepository = roleRepository;
        _currentTenant = currentTenant;
        _claimsBuilder = claimsBuilder;
    }

    public async Task<RefreshTokenGrantResult> HandleAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        // 从令牌中提取用户 ID
        var subjects = principal.FindAll(OpenIddictConstants.Claims.Subject).Select(c => c.Value).ToArray();
        if (subjects.Length != 1 || !Guid.TryParse(subjects[0], out var userId))
        {
            _logger.LogWarning("RefreshToken grant: subject claim missing or invalid.");
            return RefreshTokenGrantResult.Fail("刷新令牌无效");
        }

        var tenantId = principal.FindFirst("tenant_id")?.Value;
        var userName = principal.FindFirst(OpenIddictConstants.Claims.Name)?.Value;

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

        var tokenTenants = principal.FindAll("tenant_id").Select(c => c.Value).ToArray();
        var aliases = principal.FindAll("tenantid").Select(c => c.Value).ToArray();
        // Host accounts refresh only in host context; there is no implicit host bypass for tenant users.
        if (tokenTenants.Length != 1 || aliases.Length > 1 ||
            (aliases.Length == 1 && !string.Equals(aliases[0], tokenTenants[0], StringComparison.Ordinal)) ||
            !string.Equals(NormalizeTenant(tokenTenants[0]), NormalizeTenant(user.TenantId), StringComparison.Ordinal) ||
            !string.Equals(NormalizeTenant(user.TenantId), NormalizeTenant(_currentTenant.Id), StringComparison.Ordinal))
        {
            await _securityLogService.WriteAsync(userId, user.UserName, user.TenantId,
                "RefreshToken", false, "租户不匹配", cancellationToken);
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

        var roles = await _roleRepository.GetByUserIdAsync(user.Id, cancellationToken);
        var claims = _claimsBuilder.Build(new IdentityClaimsContext
        {
            UserId = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            TenantId = user.TenantId,
            OrganizationId = user.OrganizationId,
            IsSuperAdmin = user.IsSuperAdmin,
            Roles = roles.Where(r => r.IsActive && string.Equals(NormalizeTenant(r.TenantId), NormalizeTenant(user.TenantId), StringComparison.Ordinal))
                .Select(r => r.Name).ToArray(),
            Permissions = Array.Empty<string>()
        });
        // Preserve only OpenIddict's reserved protocol namespace (nonce, lifetime overrides, etc.).
        // The official SignIn pipeline replaces derived token identities/timestamps on issuance.
        var identity = new ClaimsIdentity(principal.Claims
            .Where(c => c.Type.StartsWith(OpenIddictConstants.Claims.Prefixes.Private, StringComparison.Ordinal))
            .Select(c => c.Clone()), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        identity.AddClaims(claims);
        var refreshed = new ClaimsPrincipal(identity);
        // Copy protocol binding explicitly. No application authority claims survive from the old token.
        refreshed.SetScopes(principal.GetScopes());
        refreshed.SetResources(principal.GetResources());
        refreshed.SetAuthorizationId(principal.GetAuthorizationId());
        refreshed.SetPresenters(principal.GetPresenters());
        refreshed.SetDestinations(_ => new[] { OpenIddictConstants.Destinations.AccessToken });

        // 记录安全日志
        await _securityLogService.WriteAsync(
            userId: userId,
            userName: user.UserName,
            tenantId: user.TenantId,
            action: "RefreshToken",
            isSucceeded: true,
            cancellationToken: cancellationToken);

        return RefreshTokenGrantResult.Success(refreshed);
    }

    private static string? NormalizeTenant(string? tenantId) => string.IsNullOrEmpty(tenantId) ? null : tenantId;
}
