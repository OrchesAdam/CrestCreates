using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    /// <summary>
    /// EF Core 数据库事务包装器
    /// 提供对 EF Core 事务的统一抽象访问
    /// </summary>
    public class EfCoreDataBaseTransaction : IDataBaseTransaction
    {
        private readonly IDbContextTransaction _transaction;
        private readonly DbContext _dbContext;
        private bool _isCommitted = false;
        private bool _isRolledBack = false;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="transaction">EF Core 原生事务对象</param>
        /// <param name="dbContext">关联的数据库上下文</param>
        /// <exception cref="ArgumentNullException">当参数为 null 时抛出</exception>
        public EfCoreDataBaseTransaction(IDbContextTransaction transaction, DbContext dbContext)
        {
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            TransactionId = Guid.NewGuid();
        }

        /// <inheritdoc />
        public Guid TransactionId { get; }

        /// <inheritdoc />
        /// <remarks>
        /// 从底层的 DbTransaction 获取隔离级别
        /// IDbContextTransaction 本身没有 IsolationLevel 属性，
        /// 需要通过 IInfrastructure 接口访问底层的 ADO.NET DbTransaction
        /// </remarks>
        public IsolationLevel IsolationLevel
        {
            get
            {
                // EF Core 的 IDbContextTransaction 实现了 IInfrastructure<DbTransaction>
                // 通过它可以访问底层的 ADO.NET DbTransaction，而 DbTransaction 有 IsolationLevel 属性
                if (_transaction is IInfrastructure<DbTransaction> infrastructure)
                {
                    return infrastructure.Instance.IsolationLevel;
                }

                // 如果无法获取，返回 Unspecified
                return IsolationLevel.Unspecified;
            }
        }

        /// <inheritdoc />
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_isCommitted || _isRolledBack)
            {
                throw new InvalidOperationException(
                    $"Transaction {TransactionId} has already been completed. " +
                    $"IsCommitted: {_isCommitted}, IsRolledBack: {_isRolledBack}");
            }

            try
            {
                await _transaction.CommitAsync(cancellationToken);
                _isCommitted = true;
            }
            catch (Exception)
            {
                // 如果提交失败，尝试回滚
                if (!_isRolledBack)
                {
                    try
                    {
                        await _transaction.RollbackAsync(cancellationToken);
                        _isRolledBack = true;
                    }
                    catch
                    {
                        // 忽略回滚异常，重新抛出原始异常
                    }
                }
                throw;
            }
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_isCommitted || _isRolledBack)
            {
                throw new InvalidOperationException(
                    $"Transaction {TransactionId} has already been completed. " +
                    $"IsCommitted: {_isCommitted}, IsRolledBack: {_isRolledBack}");
            }

            await _transaction.RollbackAsync(cancellationToken);
            _isRolledBack = true;
        }

        /// <inheritdoc />
        public object GetNativeTransaction() => _transaction;

        /// <inheritdoc />
        public bool IsCommitted => _isCommitted;

        /// <inheritdoc />
        public bool IsRolledBack => _isRolledBack;

        /// <inheritdoc />
        public bool IsCompleted => _isCommitted || _isRolledBack;

        /// <inheritdoc />
        public void Dispose()
        {
            // 只释放事务，不释放 DbContext
            // DbContext 的生命周期由 DI 容器或调用者管理
            _transaction?.Dispose();
        }
    }
}