using System;
using System.Data;
using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrestCreates.Data.EFCore.UnitOfWork;

/// <summary>
/// EF Core 的 CAP 参与 lease 签发者：解析当前有效资源（受管链路由后）的数据库事务。
/// </summary>
internal sealed class EfCoreTransactionLeaseProvider : IUnitOfWorkTransactionLeaseProvider
{
    private readonly IDataBaseContext _dataBaseContext;

    public EfCoreTransactionLeaseProvider(IDataBaseContext dataBaseContext)
    {
        _dataBaseContext = dataBaseContext;
    }

    public bool TryAcquireCurrentLease(out UnitOfWorkTransactionLease? lease)
    {
        if (_dataBaseContext.GetNativeContext() is DbContext dbContext &&
            dbContext.Database.CurrentTransaction is { } transaction)
        {
            lease = new UnitOfWorkTransactionLease(
                OrmProvider.EfCore,
                dbContext,
                new EfCoreTransactionIdentity(dbContext, transaction));
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

        public object NativeTransaction => _transaction;
    }
}
