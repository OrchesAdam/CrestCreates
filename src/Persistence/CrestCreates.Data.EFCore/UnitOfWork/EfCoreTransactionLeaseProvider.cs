using System;
using System.Data;
using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrestCreates.Data.EFCore.UnitOfWork;

/// <summary>
/// EF Core 的 CAP 参与 lease 签发者：解析当前受管窗口有效资源的数据库事务。
/// </summary>
/// <remarks>
/// 签发前置条件（与 <see cref="IEfCoreTransactionBorrower"/> 共用同一受管事务准入权威）：
/// 读方可见链上**最近的 owner 窗口**已开始、为事务型、Active、未释放，且其开始事务时
/// 记录的**实际资源/事务**与本读方解析到的 Context/Transaction 为同一关联；
/// 无 owner、未开始、非事务、终态、已释放、独立 scope/宿主、原生未受管事务与
/// 事务被替换（引用不一致）一律拒绝（「存在事务」不等于「受管参与窗口」）。
/// 公开 lease 不携带生命周期句柄；原生借用由 <see cref="IEfCoreTransactionBorrower"/> 边界提供。
/// </remarks>
internal sealed class EfCoreTransactionLeaseProvider : IUnitOfWorkTransactionLeaseProvider
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, InstanceToken> InstanceIds = new();

    private readonly IDataBaseContext _dataBaseContext;
    private readonly UnitOfWorkChainNode? _chainNode;

    public EfCoreTransactionLeaseProvider(IDataBaseContext dataBaseContext, UnitOfWorkChainNode? chainNode)
    {
        _dataBaseContext = dataBaseContext;
        _chainNode = chainNode;
    }

    private static Guid GetInstanceId(DbContext context)
        => InstanceIds.GetValue(context, _ => new InstanceToken()).Id;

    private sealed class InstanceToken
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public bool TryAcquireCurrentLease(out UnitOfWorkTransactionLease? lease)
    {
        // 受管事务准入先于一切资源检查：无有效受管窗口（或有窗口但无受管事务关联）时一律拒绝。
        if (!UnitOfWorkExecutionContext.TryGetCurrentManagedTransaction(
                _chainNode,
                out var managedTransaction,
                out var managedContext))
        {
            lease = null;
            return false;
        }

        if (_dataBaseContext.GetNativeContext() is DbContext dbContext &&
            ReferenceEquals(dbContext, managedContext) &&
            dbContext.Database.CurrentTransaction is { } transaction &&
            ReferenceEquals(transaction, managedTransaction))
        {
            var resource = new UnitOfWorkResourceIdentity(
                OrmProvider.EfCore,
                dbContext.GetType().FullName ?? dbContext.GetType().Name,
                GetInstanceId(dbContext));

            lease = new UnitOfWorkTransactionLease(resource, new EfCoreTransactionIdentity(dbContext, transaction));
            return true;
        }

        lease = null;
        return false;
    }

    private sealed class EfCoreTransactionIdentity : IUnitOfWorkTransactionIdentity
    {
        private readonly DbContext _dbContext;
        private readonly IDbContextTransaction _transaction;
        private Guid? _transactionId;
        private UnitOfWorkNativeTransactionMarker? _marker;

        public EfCoreTransactionIdentity(DbContext dbContext, IDbContextTransaction transaction)
        {
            _dbContext = dbContext;
            _transaction = transaction;
        }

        public Guid TransactionId => _transactionId ??= _transaction.TransactionId;

        public bool IsCompleted
        {
            get
            {
                try
                {
                    var current = _dbContext.Database.CurrentTransaction;
                    return current is null || !ReferenceEquals(current, _transaction);
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }

        public IsolationLevel? IsolationLevel
        {
            get
            {
                try
                {
                    return _transaction.GetDbTransaction().IsolationLevel;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public object NativeTransaction => _marker ??= new UnitOfWorkNativeTransactionMarker(TransactionId);
    }
}
