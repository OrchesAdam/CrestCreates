using System.Data;

namespace CrestCreates.DbContextProvider.Abstract;

/// <summary>
/// 事务选项
/// </summary>
public class TransactionOptions
{
    /// <summary>
    /// 事务隔离级别
    /// </summary>
    public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int? Timeout { get; set; }

    /// <summary>
    /// 是否启用分布式事务
    /// </summary>
    public bool EnableDistributedTransaction { get; set; } = false;
}