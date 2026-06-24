using System;
using Microsoft.AspNetCore.Authorization;

namespace CrestCreates.Infrastructure.Authorization
{
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
}
