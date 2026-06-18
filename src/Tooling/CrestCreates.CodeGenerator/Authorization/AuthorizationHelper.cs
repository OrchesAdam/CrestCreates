using System;using System.Collections.Generic;using System.Text;

namespace CrestCreates.CodeGenerator.Authorization
{
    /// <summary>
    /// 授权逻辑公共工具类
    /// </summary>
    public static class AuthorizationHelper
    {
        /// <summary>
        /// 映射 HTTP 方法到 CRUD 权限
        /// </summary>
        public static string MapHttpMethodToPermission(string httpMethod)
        {
            if (string.IsNullOrEmpty(httpMethod))
                return string.Empty;
            
            return httpMethod.ToUpperInvariant() switch
            {
                "GET" => "View",
                "POST" => "Create",
                "PUT" => "Update",
                "DELETE" => "Delete",
                "PATCH" => "Update",
                _ => string.Empty
            };
        }

        /// <summary>
        /// 从方法名提取资源名称
        /// </summary>
        public static string GetResourceNameFromMethodName(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
                return string.Empty;
            
            if (methodName.StartsWith("Get"))
            {
                if (methodName.EndsWith("ById"))
                    return methodName.Substring(3, methodName.Length - 6);
                if (methodName.EndsWith("All"))
                    return methodName.Substring(3, methodName.Length - 7);
                if (methodName.StartsWith("GetBy"))
                    return methodName.Substring(6);
                return methodName.Substring(3);
            }
            else if (methodName.StartsWith("Create"))
            {
                return methodName.Substring(6);
            }
            else if (methodName.StartsWith("Update"))
            {
                return methodName.Substring(6);
            }
            else if (methodName.StartsWith("Delete"))
            {
                return methodName.Substring(6);
            }
            return methodName;
        }

        /// <summary>
        /// 生成权限特性代码
        /// </summary>
        public static string GeneratePermissionAttribute(string permission, bool requireAll = false)
        {
            var requireAllParam = requireAll ? ", RequireAll = true" : "";
            return $"[AuthorizePermission(\"{permission}\"{requireAllParam})]";
        }

        /// <summary>
        /// 生成角色特性代码
        /// </summary>
        public static string GenerateRoleAttribute(string[] roles)
        {
            if (roles == null || roles.Length == 0)
                return string.Empty;
            
            var roleList = string.Join("\", \"", roles);
            return $"[AuthorizeRoles(\"{roleList}\")]";
        }

        /// <summary>
        /// 生成授权特性代码
        /// </summary>
        public static string GenerateAuthorizationAttributes(
            string methodName, 
            string httpMethod, 
            string resourceName, 
            bool generateCrudPermissions, 
            string[] defaultRoles, 
            bool requireAll)
        {
            var attributes = new List<string>();

            // 生成权限特性
            if (generateCrudPermissions)
            {
                var permission = MapHttpMethodToPermission(httpMethod);
                if (!string.IsNullOrEmpty(permission))
                {
                    attributes.Add(GeneratePermissionAttribute($"{resourceName}.{permission}", requireAll));
                }
            }

            // 生成角色特性
            if (defaultRoles != null && defaultRoles.Length > 0)
            {
                attributes.Add(GenerateRoleAttribute(defaultRoles));
            }

            return string.Join("\r\n        ", attributes);
        }
    }
}
