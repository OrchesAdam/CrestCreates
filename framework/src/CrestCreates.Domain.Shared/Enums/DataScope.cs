namespace CrestCreates.Domain.Shared.Enums;

/// <summary>
/// 数据范围枚举
/// </summary>
public enum DataScope
{
    /// <summary>
    /// 仅本人数据
    /// </summary>
    Self = 1,

    /// <summary>
    /// 本组织数据
    /// </summary>
    Organization = 2,

    /// <summary>
    /// 本组织及下级组织数据
    /// </summary>
    OrganizationAndSub = 3,

    /// <summary>
    /// 全租户数据
    /// </summary>
    Tenant = 4,

    /// <summary>
    /// 全部数据（超管专用）
    /// </summary>
    All = 5
}
