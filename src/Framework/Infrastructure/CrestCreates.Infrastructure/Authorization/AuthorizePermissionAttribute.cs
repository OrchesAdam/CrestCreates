using System;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
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
}
