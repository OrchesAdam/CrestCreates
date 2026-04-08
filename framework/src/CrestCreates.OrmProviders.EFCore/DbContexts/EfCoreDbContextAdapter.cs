using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.DbContexts;

/// <summary>
/// 将业务项目中的原生 EF Core DbContext 适配为框架统一数据库上下文。
/// </summary>
public class EfCoreDbContextAdapter : IEntityFrameworkCoreDbContext
{
    private readonly DbContext _dbContext;

    public EfCoreDbContextAdapter(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public OrmProvider Provider => OrmProvider.EfCore;

    public IDataBaseTransaction CurrentTransaction =>
        _dbContext.Database.CurrentTransaction != null
            ? new EfCoreDataBaseTransaction(_dbContext.Database.CurrentTransaction, _dbContext)
            : null!;

    public string ConnectionString => _dbContext.Database.GetConnectionString() ?? string.Empty;

    public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new EfCoreDataBaseSet<TEntity>(_dbContext.Set<TEntity>());
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfCoreDataBaseTransaction(transaction, _dbContext);
    }

    public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
    {
        return new EfCoreQueryableBuilder<TEntity>(_dbContext.Set<TEntity>());
    }

    public Task<int> ExecuteSqlRawAsync(
        string sql,
        IEnumerable<object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Database.ExecuteSqlRawAsync(sql, parameters ?? Array.Empty<object>(), cancellationToken);
    }

    public object GetNativeContext()
    {
        return _dbContext;
    }

    public void Dispose()
    {
        // DbContext 的生命周期由 DI 容器管理，这里不释放底层实例。
    }
}
