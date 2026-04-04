using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Authorization;

public class PermissionChecker : IPermissionChecker
{
    private readonly IPermissionStore _permissionStore;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<PermissionChecker> _logger;

    public PermissionChecker(
        IPermissionStore permissionStore,
        ICurrentPrincipalAccessor principalAccessor,
        ICurrentUser currentUser,
        ILogger<PermissionChecker> logger)
    {
        _permissionStore = permissionStore;
        _principalAccessor = principalAccessor;
        _currentUser = currentUser;
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

        var result = await _permissionStore.IsGrantedAsync(principal, permissionName);

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

        foreach (var permissionName in permissionNames)
        {
            result[permissionName] = await _permissionStore.IsGrantedAsync(principal, permissionName);
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
}
