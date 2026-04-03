namespace CrestCreates.Domain.Shared.Enums;

/// <summary>
/// 过滤操作符枚举
/// </summary>
public enum FilterOperator
{
    /// <summary>
    /// 等于
    /// </summary>
    Equals,

    /// <summary>
    /// 不等于
    /// </summary>
    NotEquals,

    /// <summary>
    /// 包含
    /// </summary>
    Contains,

    /// <summary>
    /// 以...开头
    /// </summary>
    StartsWith,

    /// <summary>
    /// 以...结尾
    /// </summary>
    EndsWith,

    /// <summary>
    /// 大于
    /// </summary>
    GreaterThan,

    /// <summary>
    /// 大于或等于
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// 小于
    /// </summary>
    LessThan,

    /// <summary>
    /// 小于或等于
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// 在集合中
    /// </summary>
    In,

    /// <summary>
    /// 不在集合中
    /// </summary>
    NotIn,

    /// <summary>
    /// 为空
    /// </summary>
    IsNull,

    /// <summary>
    /// 不为空
    /// </summary>
    IsNotNull
}