namespace CrestCreates.DbContextProvider.Abstract;

/// <summary>
/// 事务传播行为
/// </summary>
public enum TransactionPropagation
{
    /// <summary>
    /// 必需的：如果存在事务则加入，否则创建新事务
    /// </summary>
    Required,

    /// <summary>
    /// 需要新事务：总是创建新事务，如果存在事务则挂起
    /// </summary>
    RequiresNew,

    /// <summary>
    /// 支持：如果存在事务则加入，否则以非事务方式执行
    /// </summary>
    Supports,

    /// <summary>
    /// 不支持：总是以非事务方式执行，如果存在事务则挂起
    /// </summary>
    NotSupported,

    /// <summary>
    /// 强制：必须在现有事务中执行，否则抛出异常
    /// </summary>
    Mandatory,

    /// <summary>
    /// 从不：以非事务方式执行，如果存在事务则抛出异常
    /// </summary>
    Never,

    /// <summary>
    /// 嵌套：如果存在事务则创建嵌套事务，否则创建新事务
    /// </summary>
    Nested
}