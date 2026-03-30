using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Authorization
{
    /// <summary>
    /// 权限授予信息
    /// </summary>
    public class PermissionGrant
    {
        /// <summary>
        /// 权限名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 提供者名称（User/Role/其他）
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// 提供者键值（用户ID/角色名）
        /// </summary>
        public string ProviderKey { get; set; }

        public PermissionGrant(string name, string providerName, string providerKey)
        {
            Name = name;
            ProviderName = providerName;
            ProviderKey = providerKey;
        }
    }

    /// <summary>
    /// 基于内存的权限存储（用于开发和测试）
    /// </summary>
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

            // 检查用户直接权限
            if (!string.IsNullOrEmpty(userId) &&
                _userPermissions.TryGetValue(userId, out var userPerms) &&
                userPerms.Contains(permissionName))
            {
                _logger.LogDebug("Permission '{Permission}' granted to user '{UserId}'", permissionName, userId);
                return Task.FromResult(true);
            }

            // 检查角色权限
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

        /// <summary>
        /// 为用户授予权限
        /// </summary>
        public void GrantToUser(string userId, params string[] permissions)
        {
            var userPerms = _userPermissions.GetOrAdd(userId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var permission in permissions)
            {
                userPerms.Add(permission);
            }
            _logger.LogInformation("Granted {Count} permissions to user '{UserId}'", permissions.Length, userId);
        }

        /// <summary>
        /// 撤销用户权限
        /// </summary>
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

        /// <summary>
        /// 为角色授予权限
        /// </summary>
        public void GrantToRole(string roleName, params string[] permissions)
        {
            var rolePerms = _rolePermissions.GetOrAdd(roleName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var permission in permissions)
            {
                rolePerms.Add(permission);
            }
            _logger.LogInformation("Granted {Count} permissions to role '{Role}'", permissions.Length, roleName);
        }

        /// <summary>
        /// 撤销角色权限
        /// </summary>
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

        /// <summary>
        /// 获取用户所有权限
        /// </summary>
        public string[] GetUserPermissions(string userId)
        {
            return _userPermissions.TryGetValue(userId, out var perms)
                ? perms.ToArray()
                : Array.Empty<string>();
        }

        /// <summary>
        /// 获取角色所有权限
        /// </summary>
        public string[] GetRolePermissions(string roleName)
        {
            return _rolePermissions.TryGetValue(roleName, out var perms)
                ? perms.ToArray()
                : Array.Empty<string>();
        }

        /// <summary>
        /// 清空所有权限
        /// </summary>
        public void Clear()
        {
            _userPermissions.Clear();
            _rolePermissions.Clear();
            _logger.LogWarning("All permissions cleared");
        }
    }

    /// <summary>
    /// 带缓存的权限存储装饰器
    /// </summary>
    public class CachedPermissionStore : IPermissionStore
    {
        private readonly IPermissionStore _innerStore;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CachedPermissionStore> _logger;
        private readonly TimeSpan _cacheDuration;

        public CachedPermissionStore(
            IPermissionStore innerStore,
            IMemoryCache cache,
            ILogger<CachedPermissionStore> logger,
            TimeSpan? cacheDuration = null)
        {
            _innerStore = innerStore;
            _cache = cache;
            _logger = logger;
            _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(20);
        }

        public async Task<bool> IsGrantedAsync(ClaimsPrincipal principal, string permissionName)
        {
            var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal?.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return await _innerStore.IsGrantedAsync(principal, permissionName);
            }

            var cacheKey = $"Permission:{userId}:{permissionName}";

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
                
                var result = await _innerStore.IsGrantedAsync(principal, permissionName);
                
                _logger.LogDebug("Cached permission check result for user '{UserId}', permission '{Permission}': {Result}",
                    userId, permissionName, result);
                
                return result;
            });
        }

        /// <summary>
        /// 清除用户权限缓存
        /// </summary>
        public void ClearCache(string userId)
        {
            // Note: IMemoryCache 不支持按模式删除，实际应用中可能需要使用 Redis 等支持模式删除的缓存
            _logger.LogInformation("Cache clear requested for user '{UserId}'", userId);
        }
    }

    /// <summary>
    /// 权限授予服务接口
    /// </summary>
    public interface IPermissionGrantService
    {
        Task GrantAsync(string permissionName, string providerName, string providerKey);
        Task RevokeAsync(string permissionName, string providerName, string providerKey);
        Task<bool> IsGrantedAsync(string permissionName, string providerName, string providerKey);
        Task<List<PermissionGrant>> GetAllAsync(string providerName, string providerKey);
    }

    /// <summary>
    /// 权限授予服务实现
    /// </summary>
    public class PermissionGrantService : IPermissionGrantService
    {
        private readonly InMemoryPermissionStore _store;
        private readonly ILogger<PermissionGrantService> _logger;

        public PermissionGrantService(
            IPermissionStore store,
            ILogger<PermissionGrantService> logger)
        {
            // 尝试获取内部存储
            _store = (store as CachedPermissionStore != null)
                ? GetInnerStore(store as CachedPermissionStore)
                : store as InMemoryPermissionStore;
            
            _logger = logger;
        }

        private InMemoryPermissionStore GetInnerStore(CachedPermissionStore cachedStore)
        {
            var field = cachedStore.GetType().GetField("_innerStore",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(cachedStore) as InMemoryPermissionStore;
        }

        public Task GrantAsync(string permissionName, string providerName, string providerKey)
        {
            if (_store == null)
            {
                throw new NotSupportedException("Current permission store does not support grant operations");
            }

            if (providerName.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                _store.GrantToUser(providerKey, permissionName);
            }
            else if (providerName.Equals("Role", StringComparison.OrdinalIgnoreCase))
            {
                _store.GrantToRole(providerKey, permissionName);
            }
            else
            {
                throw new NotSupportedException($"Provider '{providerName}' is not supported");
            }

            _logger.LogInformation("Granted permission '{Permission}' to {Provider} '{Key}'",
                permissionName, providerName, providerKey);

            return Task.CompletedTask;
        }

        public Task RevokeAsync(string permissionName, string providerName, string providerKey)
        {
            if (_store == null)
            {
                throw new NotSupportedException("Current permission store does not support revoke operations");
            }

            if (providerName.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                _store.RevokeFromUser(providerKey, permissionName);
            }
            else if (providerName.Equals("Role", StringComparison.OrdinalIgnoreCase))
            {
                _store.RevokeFromRole(providerKey, permissionName);
            }
            else
            {
                throw new NotSupportedException($"Provider '{providerName}' is not supported");
            }

            _logger.LogInformation("Revoked permission '{Permission}' from {Provider} '{Key}'",
                permissionName, providerName, providerKey);

            return Task.CompletedTask;
        }

        public Task<bool> IsGrantedAsync(string permissionName, string providerName, string providerKey)
        {
            if (_store == null)
            {
                return Task.FromResult(false);
            }

            string[] permissions;

            if (providerName.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                permissions = _store.GetUserPermissions(providerKey);
            }
            else if (providerName.Equals("Role", StringComparison.OrdinalIgnoreCase))
            {
                permissions = _store.GetRolePermissions(providerKey);
            }
            else
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(permissions.Contains(permissionName, StringComparer.OrdinalIgnoreCase));
        }

        public Task<List<PermissionGrant>> GetAllAsync(string providerName, string providerKey)
        {
            if (_store == null)
            {
                return Task.FromResult(new List<PermissionGrant>());
            }

            string[] permissions;

            if (providerName.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                permissions = _store.GetUserPermissions(providerKey);
            }
            else if (providerName.Equals("Role", StringComparison.OrdinalIgnoreCase))
            {
                permissions = _store.GetRolePermissions(providerKey);
            }
            else
            {
                return Task.FromResult(new List<PermissionGrant>());
            }

            var grants = permissions.Select(p => new PermissionGrant(p, providerName, providerKey)).ToList();
            return Task.FromResult(grants);
        }
    }
}
