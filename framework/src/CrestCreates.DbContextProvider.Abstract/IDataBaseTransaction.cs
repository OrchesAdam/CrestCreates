using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.DbContextProvider.Abstract
{
    /// <summary>
    /// 数据库事务统一抽象接口
    /// </summary>
    /// <remarks>
    /// 提供跨 ORM 的统一事务管理接口
    /// 支持事务的提交、回滚和释放
    /// </remarks>
    public interface IDataBaseTransaction : IDisposable
    {
        /// <summary>
        /// 事务 ID
        /// </summary>
        Guid TransactionId { get; }

        /// <summary>
        /// 事务隔离级别
        /// </summary>
        IsolationLevel IsolationLevel { get; }

        /// <summary>
        /// 提交事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 回滚事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task RollbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取原生事务对象
        /// </summary>
        /// <remarks>
        /// 用于访问特定 ORM 的原生事务功能
        /// EF Core: IDbContextTransaction
        /// FreeSql: DbTransaction
        /// SqlSugar: ITenant 或 DbTransaction
        /// </remarks>
        object GetNativeTransaction();

        /// <summary>
        /// 事务是否已提交
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// 事务是否已回滚
        /// </summary>
        bool IsRolledBack { get; }

        /// <summary>
        /// 事务是否已完成（已提交或已回滚）
        /// </summary>
        bool IsCompleted { get; }
    }
}
