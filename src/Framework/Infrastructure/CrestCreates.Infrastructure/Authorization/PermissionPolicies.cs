using System.Linq;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Infrastructure.Authorization;

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

    /// <summary>
    /// 创建权限策略名称（类型安全重载）
    /// </summary>
    public static string CreatePolicyName(params PermissionName[] permissions)
    {
        return CreatePolicyName(permissions.Select(p => p.RequireValue()).ToArray());
    }

    /// <summary>
    /// 创建需要所有权限的策略名称（类型安全重载）
    /// </summary>
    public static string CreateAllPolicyName(params PermissionName[] permissions)
    {
        return CreateAllPolicyName(permissions.Select(p => p.RequireValue()).ToArray());
    }
}
