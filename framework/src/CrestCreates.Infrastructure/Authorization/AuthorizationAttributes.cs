using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Infrastructure.Authorization
{
    /// <summary>
    /// 权限授权特性
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class AuthorizePermissionAttribute : Attribute, IAsyncAuthorizationFilter
    {
        /// <summary>
        /// 权限名称
        /// </summary>
        public string[] Permissions { get; }

        /// <summary>
        /// 是否需要所有权限（true: AND, false: OR）
        /// </summary>
        public bool RequireAll { get; set; }

        public AuthorizePermissionAttribute(params string[] permissions)
        {
            Permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
            RequireAll = false;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            // 检查是否已认证
            if (context.HttpContext.User?.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            var permissionChecker = context.HttpContext.RequestServices.GetRequiredService<IPermissionChecker>();

            if (RequireAll)
            {
                // 需要所有权限
                var result = await permissionChecker.IsGrantedAsync(Permissions);
                if (!result.AllGranted)
                {
                    context.Result = new ForbidResult();
                    return;
                }
            }
            else
            {
                // 只需要任一权限
                var result = await permissionChecker.IsGrantedAsync(Permissions);
                if (result.AllProhibited)
                {
                    context.Result = new ForbidResult();
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 角色授权特性（简化版）
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class AuthorizeRolesAttribute : AuthorizeAttribute
    {
        public AuthorizeRolesAttribute(params string[] roles)
        {
            Roles = string.Join(",", roles);
        }
    }

    /// <summary>
    /// 权限授权处理器
    /// </summary>
    public class PermissionAuthorizationRequirement : IAuthorizationRequirement
    {
        public string[] Permissions { get; }
        public bool RequireAll { get; }

        public PermissionAuthorizationRequirement(string[] permissions, bool requireAll = false)
        {
            Permissions = permissions;
            RequireAll = requireAll;
        }
    }

    /// <summary>
    /// 权限授权处理器
    /// </summary>
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionAuthorizationRequirement>
    {
        private readonly IPermissionChecker _permissionChecker;

        public PermissionAuthorizationHandler(IPermissionChecker permissionChecker)
        {
            _permissionChecker = permissionChecker;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionAuthorizationRequirement requirement)
        {
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                context.Fail();
                return;
            }

            if (requirement.RequireAll)
            {
                // 需要所有权限
                var result = await _permissionChecker.IsGrantedAsync(
                    context.User,
                    requirement.Permissions);

                if (result.AllGranted)
                {
                    context.Succeed(requirement);
                }
                else
                {
                    context.Fail();
                }
            }
            else
            {
                // 只需要任一权限
                var result = await _permissionChecker.IsGrantedAsync(
                    context.User,
                    requirement.Permissions);

                if (!result.AllProhibited)
                {
                    context.Succeed(requirement);
                }
                else
                {
                    context.Fail();
                }
            }
        }
    }

    /// <summary>
    /// 权限策略提供者
    /// </summary>
    public static class PermissionPolicies
    {
        /// <summary>
        /// 创建权限策略名称
        /// </summary>
        public static string CreatePolicyName(params string[] permissions)
        {
            return $"Permission:{string.Join(",", permissions)}";
        }

        /// <summary>
        /// 创建需要所有权限的策略名称
        /// </summary>
        public static string CreateAllPolicyName(params string[] permissions)
        {
            return $"PermissionAll:{string.Join(",", permissions)}";
        }
    }
}
