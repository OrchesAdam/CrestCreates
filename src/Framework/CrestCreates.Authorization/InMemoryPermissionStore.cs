using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Authorization;

public class InMemoryPermissionStore : IPermissionStore
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _userPermissions;
    private readonly ConcurrentDictionary<string, HashSet<string>> _rolePermissions;
    private readonly ILogger<InMemoryPermissionStore> _logger;

    public InMemoryPermissionStore(ILogger<InMemoryPermissionStore> logger)
    {
        _userPermissions = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _rolePermissions = new ConcurrentDictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
    {
        if (principal == null || !principal.Identity.IsAuthenticated)
        {
            return Task.FromResult(false);
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId) &&
            _userPermissions.TryGetValue(userId, out var userPerms) &&
            userPerms.Contains(permissionName))
        {
            _logger.LogDebug("Permission '{Permission}' granted to user '{UserId}'", permissionName, userId);
            return Task.FromResult(true);
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        foreach (var role in roles)
        {
            if (_rolePermissions.TryGetValue(role, out var rolePerms) &&
                rolePerms.Contains(permissionName))
            {
                _logger.LogDebug("Permission '{Permission}' granted to role '{Role}'", permissionName, role);
                return Task.FromResult(true);
            }
        }

        _logger.LogDebug("Permission '{Permission}' not granted to user '{UserId}'", permissionName, userId);
        return Task.FromResult(false);
    }

    public void GrantToUser(string userId, params string[] permissions)
    {
        var userPerms = _userPermissions.GetOrAdd(userId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var permission in permissions)
        {
            userPerms.Add(permission);
        }
        _logger.LogInformation("Granted {Count} permissions to user '{UserId}'", permissions.Length, userId);
    }

    public void RevokeFromUser(string userId, params string[] permissions)
    {
        if (_userPermissions.TryGetValue(userId, out var userPerms))
        {
            foreach (var permission in permissions)
            {
                userPerms.Remove(permission);
            }
            _logger.LogInformation("Revoked {Count} permissions from user '{UserId}'", permissions.Length, userId);
        }
    }

    public void GrantToRole(string roleName, params string[] permissions)
    {
        var rolePerms = _rolePermissions.GetOrAdd(roleName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var permission in permissions)
        {
            rolePerms.Add(permission);
        }
        _logger.LogInformation("Granted {Count} permissions to role '{Role}'", permissions.Length, roleName);
    }

    public void RevokeFromRole(string roleName, params string[] permissions)
    {
        if (_rolePermissions.TryGetValue(roleName, out var rolePerms))
        {
            foreach (var permission in permissions)
            {
                rolePerms.Remove(permission);
            }
            _logger.LogInformation("Revoked {Count} permissions from role '{Role}'", permissions.Length, roleName);
        }
    }

    public string[] GetUserPermissions(string userId)
    {
        return _userPermissions.TryGetValue(userId, out var perms)
            ? perms.ToArray()
            : Array.Empty<string>();
    }

    public string[] GetRolePermissions(string roleName)
    {
        return _rolePermissions.TryGetValue(roleName, out var perms)
            ? perms.ToArray()
            : Array.Empty<string>();
    }

    public void Clear()
    {
        _userPermissions.Clear();
        _rolePermissions.Clear();
        _logger.LogWarning("All permissions cleared");
    }
}
