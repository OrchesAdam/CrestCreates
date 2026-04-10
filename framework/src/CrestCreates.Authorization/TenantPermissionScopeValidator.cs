using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Authorization;

public class TenantPermissionScopeValidator
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILogger<TenantPermissionScopeValidator> _logger;

    public TenantPermissionScopeValidator(
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        ITenantProvider tenantProvider,
        ILogger<TenantPermissionScopeValidator> logger)
    {
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _tenantProvider = tenantProvider;
        _logger = logger;
    }

    public async Task<TenantPermissionScopeValidationResult> ValidateAsync(
        PermissionGrantInfo grant,
        CancellationToken cancellationToken = default)
    {
        if (grant.Scope == PermissionGrantScope.Global)
        {
            return TenantPermissionScopeValidationResult.Allowed();
        }

        if (_currentUser.IsSuperAdmin)
        {
            _logger.LogDebug("超级管理员跨租户权限访问被允许: {Permission}", grant.PermissionName);
            return TenantPermissionScopeValidationResult.Allowed(isSuperAdminOverride: true);
        }

        if (string.IsNullOrEmpty(grant.TenantId))
        {
            return TenantPermissionScopeValidationResult.Denied("Tenant-scoped permission requires TenantId");
        }

        var currentTenantId = _currentTenant.Id;
        if (string.IsNullOrEmpty(currentTenantId))
        {
            return TenantPermissionScopeValidationResult.Denied("当前请求没有租户上下文");
        }

        if (!string.Equals(currentTenantId, grant.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "租户权限范围校验失败: 权限 {Permission} 的租户 {GrantTenantId} 与当前上下文 {CurrentTenantId} 不匹配",
                grant.PermissionName,
                grant.TenantId,
                currentTenantId);

            return TenantPermissionScopeValidationResult.Denied(
                $"权限 '{grant.PermissionName}' 不属于当前租户");
        }

        return TenantPermissionScopeValidationResult.Allowed();
    }

    public async Task<bool> CanGrantPermissionToTenantAsync(
        string targetTenantId,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.IsSuperAdmin)
        {
            return true;
        }

        var currentTenantId = _currentTenant.Id;
        if (string.IsNullOrEmpty(currentTenantId))
        {
            return false;
        }

        return string.Equals(currentTenantId, targetTenantId, StringComparison.OrdinalIgnoreCase);
    }
}

public class TenantPermissionScopeValidationResult
{
    public bool IsAllowed { get; set; }
    public string? FailureReason { get; set; }
    public bool IsSuperAdminOverride { get; set; }

    public static TenantPermissionScopeValidationResult Allowed(bool isSuperAdminOverride = false) => new()
    {
        IsAllowed = true,
        IsSuperAdminOverride = isSuperAdminOverride
    };

    public static TenantPermissionScopeValidationResult Denied(string reason) => new()
    {
        IsAllowed = false,
        FailureReason = reason
    };
}
