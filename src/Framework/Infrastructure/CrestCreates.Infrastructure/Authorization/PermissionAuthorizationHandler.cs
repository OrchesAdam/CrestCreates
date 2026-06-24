using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace CrestCreates.Infrastructure.Authorization
{
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
}
