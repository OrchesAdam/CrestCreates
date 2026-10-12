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
/// 签发前置条件：读方可见链上存在 **Active 且未释放** 的受管 owner 窗口，
/// 且该窗口有效资源持有活动事务；未开始、终态、已释放、独立 scope/宿主与
/// 原生未受管事务一律拒绝（「存在事务」不等于「受管参与窗口」）。
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
        // 受管窗口检查先于事务检查：无活动受管 owner 窗口时一律拒绝。
        if (!UnitOfWorkExecutionContext.TryGetCurrentManagedState(_chainNode, out var state, out var released) ||
            state != UnitOfWorkState.Active ||
            released)
        {
            lease = null;
            return false;
        }

        if (_dataBaseContext.GetNativeContext() is DbContext dbContext &&
            dbContext.Database.CurrentTransaction is { } transaction)
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
