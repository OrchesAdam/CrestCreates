using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.Infrastructure.Authorization
{
    /// <summary>
    /// 当前用户主体访问器实现
    /// </summary>
    public class CurrentPrincipalAccessor : ICurrentPrincipalAccessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly AsyncLocal<ClaimsPrincipal> _currentPrincipal = new AsyncLocal<ClaimsPrincipal>();

        public CurrentPrincipalAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public ClaimsPrincipal Principal
        {
            get
            {
                // 优先使用手动设置的 Principal
                if (_currentPrincipal.Value != null)
                {
                    return _currentPrincipal.Value;
                }

                // 从 HTTP 上下文获取
                return _httpContextAccessor?.HttpContext?.User;
            }
        }

        public IDisposable Change(ClaimsPrincipal principal)
        {
            var oldPrincipal = _currentPrincipal.Value;
            _currentPrincipal.Value = principal;

            return new DisposeAction(() =>
            {
                _currentPrincipal.Value = oldPrincipal;
            });
        }

        private class DisposeAction : IDisposable
        {
            private readonly Action _action;

            public DisposeAction(Action action)
            {
                _action = action;
            }

            public void Dispose()
            {
                _action();
            }
        }
    }

    /// <summary>
    /// 当前用户访问器（简化版）
    /// 提供用户ID、用户名、角色等快捷访问
    /// </summary>
    public interface ICurrentUser
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        string Id { get; }

        /// <summary>
        /// 用户名
        /// </summary>
        string UserName { get; }

        /// <summary>
        /// 是否已认证
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// 租户ID
        /// </summary>
        string TenantId { get; }

        /// <summary>
        /// 角色列表
        /// </summary>
        string[] Roles { get; }

        /// <summary>
        /// 查找声明值
        /// </summary>
        string FindClaimValue(string claimType);

        /// <summary>
        /// 查找所有声明值
        /// </summary>
        string[] FindClaimValues(string claimType);

        /// <summary>
        /// 是否在指定角色中
        /// </summary>
        bool IsInRole(string roleName);
    }

    /// <summary>
    /// 当前用户实现
    /// </summary>
    public class CurrentUser : ICurrentUser
    {
        private readonly ICurrentPrincipalAccessor _principalAccessor;

        public CurrentUser(ICurrentPrincipalAccessor principalAccessor)
        {
            _principalAccessor = principalAccessor;
        }

        public string Id => FindClaimValue(ClaimTypes.NameIdentifier)
            ?? FindClaimValue("sub")
            ?? FindClaimValue("uid");

        public string UserName => FindClaimValue(ClaimTypes.Name)
            ?? FindClaimValue("preferred_username")
            ?? FindClaimValue("name");

        public bool IsAuthenticated => _principalAccessor.Principal?.Identity?.IsAuthenticated ?? false;

        public string TenantId => FindClaimValue("tenantid")
            ?? FindClaimValue("tenant_id")
            ?? FindClaimValue("TenantId");

        public string[] Roles
        {
            get
            {
                var roles = FindClaimValues(ClaimTypes.Role);
                if (roles.Length > 0)
                    return roles;

                return FindClaimValues("role");
            }
        }

        public string FindClaimValue(string claimType)
        {
            return _principalAccessor.Principal?.FindFirst(claimType)?.Value;
        }

        public string[] FindClaimValues(string claimType)
        {
            var claims = _principalAccessor.Principal?.FindAll(claimType);
            if (claims == null)
                return Array.Empty<string>();

            var values = new System.Collections.Generic.List<string>();
            foreach (var claim in claims)
            {
                if (!string.IsNullOrEmpty(claim.Value))
                {
                    values.Add(claim.Value);
                }
            }

            return values.ToArray();
        }

        public bool IsInRole(string roleName)
        {
            return _principalAccessor.Principal?.IsInRole(roleName) ?? false;
        }
    }
}
