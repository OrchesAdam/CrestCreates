using Microsoft.AspNetCore.Authorization;

namespace CrestCreates.Infrastructure.Authorization
{
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
}
