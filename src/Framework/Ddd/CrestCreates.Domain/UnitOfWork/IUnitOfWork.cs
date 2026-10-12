using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Domain.UnitOfWork
{
    /// <summary>
    /// 内核解析后的强类型事务开始选项（Provider 不再从环境读取配置）。
    /// </summary>
    public sealed class UnitOfWorkBeginOptions
    {
        /// <summary>
        /// 生效的隔离级别；null 表示未指定（使用 Provider 默认）。
        /// </summary>
        public IsolationLevel? IsolationLevel { get; init; }

        /// <summary>
        /// 剩余截止时间；null 表示无额外截止。Provider 只在实际支持时映射，不支持必须声明。
        /// </summary>
        public TimeSpan? Timeout { get; init; }
    }

    public interface IUnitOfWork : IDisposable
    {
        Task BeginTransactionAsync(UnitOfWorkBeginOptions options, CancellationToken cancellationToken = default);
        Task CommitTransactionAsync(CancellationToken cancellationToken = default);
        Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
