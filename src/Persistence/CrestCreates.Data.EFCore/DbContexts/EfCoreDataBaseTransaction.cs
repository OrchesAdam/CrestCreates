using System;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.Data.EFCore.DbContexts
{
    /// <summary>
    /// EF Core 数据库事务包装器
    /// 提供对 EF Core 事务的统一抽象访问
    /// </summary>
    /// <remarks>
    /// 同一原生事务的所有包装器共享身份与终态：提交/回滚事实以原生事务为键记录，
    /// 重复读取 <see cref="IDataBaseContext.CurrentTransaction"/> 得到同一实例。
    /// 提交派发期失败视为结果未知，不再由包装器尝试回滚并宣称已回滚。
    /// </remarks>
    public class EfCoreDataBaseTransaction : IDataBaseTransaction
    {
        private static readonly ConditionalWeakTable<IDbContextTransaction, TransactionFacts> Facts = new();
        private static readonly ConditionalWeakTable<IDbContextTransaction, EfCoreDataBaseTransaction> Wrappers = new();

        private readonly IDbContextTransaction _transaction;
        private readonly DbContext _dbContext;
        private readonly TransactionFacts _facts;

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
            _facts = Facts.GetValue(transaction, static _ => new TransactionFacts());
        }

        /// <summary>
        /// 同一原生事务返回同一包装器（身份与终态跨读取一致）。
        /// </summary>
        internal static EfCoreDataBaseTransaction GetOrCreate(
            IDbContextTransaction transaction,
            DbContext dbContext)
        {
            return Wrappers.GetValue(transaction, key => new EfCoreDataBaseTransaction(key, dbContext));
        }

        internal static void MarkCommitted(IDbContextTransaction transaction)
            => Facts.GetValue(transaction, static _ => new TransactionFacts()).MarkCommitted();

        internal static void MarkRolledBack(IDbContextTransaction transaction)
            => Facts.GetValue(transaction, static _ => new TransactionFacts()).MarkRolledBack();

        internal static void MarkOutcomeUnknown(IDbContextTransaction transaction)
            => Facts.GetValue(transaction, static _ => new TransactionFacts()).MarkOutcomeUnknown();

        /// <inheritdoc />
        public Guid TransactionId => _facts.TransactionId;

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
            _facts.EnterOperation(TransactionId);

            try
            {
                await _transaction.CommitAsync(cancellationToken);
                _facts.MarkCommitted();
            }
            catch (Exception)
            {
                // 提交派发期失败/响应丢失：结果未知。回滚尝试不得把未知结果改写为确定回滚。
                _facts.MarkOutcomeUnknown();
                throw;
            }
            finally
            {
                _facts.ExitOperation();
            }
        }

        /// <inheritdoc />
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            _facts.EnterOperation(TransactionId);

            try
            {
                await _transaction.RollbackAsync(cancellationToken);
                _facts.MarkRolledBack();
            }
            catch (Exception)
            {
                // 回滚未能确认：终态保持未知，后续不得宣称已提交或已回滚。
                _facts.MarkOutcomeUnknown();
                throw;
            }
            finally
            {
                _facts.ExitOperation();
            }
        }

        /// <inheritdoc />
        public object GetNativeTransaction() => _transaction;

        /// <inheritdoc />
        public bool IsCommitted => _facts.IsCommitted;

        /// <inheritdoc />
        public bool IsRolledBack => _facts.IsRolledBack;

        /// <inheritdoc />
        public bool IsCompleted => _facts.IsCompleted;

        /// <summary>提交或回滚结果无法确认。</summary>
        public bool IsOutcomeUnknown => _facts.IsOutcomeUnknown;

        /// <inheritdoc />
        public void Dispose()
        {
            // 只释放事务，不释放 DbContext
            // DbContext 的生命周期由 DI 容器或调用者管理
            _transaction?.Dispose();
        }

        private sealed class TransactionFacts
        {
            private readonly object _sync = new();
            private int _operationInProgress;

            public Guid TransactionId { get; } = Guid.NewGuid();

            public bool IsCommitted { get; private set; }

            public bool IsRolledBack { get; private set; }

            public bool IsOutcomeUnknown { get; private set; }

            public bool IsCompleted => IsCommitted || IsRolledBack || IsOutcomeUnknown;

            public void EnterOperation(Guid transactionId)
            {
                if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
                {
                    throw new InvalidOperationException(
                        $"Transaction {transactionId} already has an operation in progress.");
                }

                lock (_sync)
                {
                    if (IsCompleted)
                    {
                        Interlocked.Exchange(ref _operationInProgress, 0);
                        throw new InvalidOperationException(
                            $"Transaction {transactionId} has already been completed. " +
                            $"IsCommitted: {IsCommitted}, IsRolledBack: {IsRolledBack}, " +
                            $"IsOutcomeUnknown: {IsOutcomeUnknown}");
                    }
                }
            }

            public void ExitOperation() => Interlocked.Exchange(ref _operationInProgress, 0);

            public void MarkCommitted()
            {
                lock (_sync)
                {
                    if (!IsCompleted)
                    {
                        IsCommitted = true;
                    }
                }
            }

            public void MarkRolledBack()
            {
                lock (_sync)
                {
                    if (!IsCompleted)
                    {
                        IsRolledBack = true;
                    }
                }
            }

            public void MarkOutcomeUnknown()
            {
                lock (_sync)
                {
                    if (!IsCompleted)
                    {
                        IsOutcomeUnknown = true;
                    }
                }
            }
        }
    }
}