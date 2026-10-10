using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Abstractions;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.Tests;

/// <summary>
/// 最小 IDataBaseContext 探针：仅用于 ambient 身份（隔离/链）断言，不执行数据操作。
/// </summary>
public sealed class TestDataBaseContext : IDataBaseContext
{
    public TestDataBaseContext(Guid scopeId)
    {
        ScopeId = scopeId;
    }

    public Guid ScopeId { get; }

    public OrmProvider Provider => OrmProvider.EfCore;

    public IDataBaseTransaction? CurrentTransaction => null;

    public string? ConnectionString => null;

    public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class => throw new NotSupportedException();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class => throw new NotSupportedException();

    public object GetNativeContext() => this;

    public void Dispose()
    {
    }
}
