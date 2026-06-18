using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Authorization;

public class PermissionChecker : IPermissionChecker
{
    private readonly IPermissionGrantManager _permissionGrantManager;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<PermissionChecker> _logger;

    public PermissionChecker(
        IPermissionGrantManager permissionGrantManager,
        ICurrentPrincipalAccessor principalAccessor,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ILogger<PermissionChecker> logger)
    {
        _permissionGrantManager = permissionGrantManager;
        _principalAccessor = principalAccessor;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public Task<bool> IsGrantedAsync(string permissionName)
    {
        return IsGrantedAsync(_principalAccessor.Principal, permissionName);
    }

    public async Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
    {
        if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
        {
            return false;
        }

        if (IsSuperAdmin(principal))
        {
            _logger.LogDebug("Permission '{Permission}' granted to super admin", permissionName);
            return true;
        }

        var permissions = await GetEffectivePermissionsAsync(principal);
        var result = permissions.Contains(permissionName, StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug("Permission '{Permission}' check result: {Result}", permissionName, result);

        return result;
    }

    public Task<MultiplePermissionGrantResult> IsGrantedAsync(string[] permissionNames)
    {
        return IsGrantedAsync(_principalAccessor.Principal, permissionNames);
    }

    public async Task<MultiplePermissionGrantResult> IsGrantedAsync(ClaimsPrincipal principal, string[] permissionNames)
    {
        var result = new Dictionary<string, bool>();

        if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
        {
            foreach (var permissionName in permissionNames)
            {
                result[permissionName] = false;
            }

            return new MultiplePermissionGrantResult(result);
        }

        if (IsSuperAdmin(principal))
        {
            foreach (var permissionName in permissionNames)
            {
                result[permissionName] = true;
            }
            return new MultiplePermissionGrantResult(result);
        }

        var permissions = await GetEffectivePermissionsAsync(principal);

        foreach (var permissionName in permissionNames)
        {
            result[permissionName] = permissions.Contains(permissionName, StringComparer.OrdinalIgnoreCase);
        }

        return new MultiplePermissionGrantResult(result);
    }

    public async Task CheckAsync(string permissionName)
    {
        if (!await IsGrantedAsync(permissionName))
        {
            _logger.LogWarning("Permission '{Permission}' denied for user '{UserId}'", permissionName, _currentUser.Id);
            throw new CrestPermissionException(permissionName);
        }
    }

    private static bool IsSuperAdmin(ClaimsPrincipal principal)
    {
        var isSuperAdminClaim = principal.FindFirst("is_super_admin");
        if (isSuperAdminClaim != null)
        {
            return bool.TryParse(isSuperAdminClaim.Value, out var isSuperAdmin) && isSuperAdmin;
        }
        return false;
    }

    private Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? principal.FindFirst("sub")?.Value
                     ?? string.Empty;

        var roleNames = principal.FindAll(ClaimTypes.Role)
            .Concat(principal.FindAll("role"))
            .Select(claim => claim.Value)
            .Where(roleName => !string.IsNullOrWhiteSpace(roleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tenantId = !string.IsNullOrWhiteSpace(_currentTenant.Id)
            ? _currentTenant.Id
            : principal.FindFirst("tenantid")?.Value
              ?? principal.FindFirst("tenant_id")?.Value
              ?? principal.FindFirst("TenantId")?.Value;

        return _permissionGrantManager.GetEffectivePermissionsAsync(userId, roleNames, tenantId);
    }
}
