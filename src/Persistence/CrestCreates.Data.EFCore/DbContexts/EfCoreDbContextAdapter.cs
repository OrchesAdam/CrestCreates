using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.Data.EFCore.DbContexts;

/// <summary>
/// 将业务项目中的原生 EF Core DbContext 适配为框架统一数据库上下文。
/// </summary>
/// <remarks>
/// 所有数据访问成员经 <see cref="EffectiveDbContext"/> 解析实际上下文：requiresNew
/// 隔离期间 <see cref="UnitOfWorkAmbientContext"/> 中由平台推入的内层上下文优先
/// （先链校验后身份校验：仅自身受管链且租户/声明/连接配置一致时跟随），
/// 使在父作用域预先注入的仓储/适配器也能跟随当前 UoW；隔离结束即恢复父上下文。
/// </remarks>
public class EfCoreDbContextAdapter : IEntityFrameworkCoreDbContext
{
    private readonly DbContext _dbContext;
    private readonly UnitOfWorkChainNode? _chainNode;
    private readonly ICurrentTenant? _currentTenant;

    public EfCoreDbContextAdapter(
        DbContext dbContext,
        UnitOfWorkChainNode? chainNode = null,
        ICurrentTenant? currentTenant = null)
    {
        _dbContext = dbContext;
        _chainNode = chainNode;
        _currentTenant = currentTenant;
    }

    public OrmProvider Provider => OrmProvider.EfCore;

    public IDataBaseTransaction? CurrentTransaction =>
        EffectiveDbContext.Database.CurrentTransaction != null
            ? new EfCoreDataBaseTransaction(EffectiveDbContext.Database.CurrentTransaction!, EffectiveDbContext)
            : null;

    public string? ConnectionString => EffectiveDbContext.Database.GetConnectionString();

    public IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class
    {
        return new EfCoreDataBaseSet<TEntity>(EffectiveDbContext.Set<TEntity>(), _chainNode);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return EffectiveDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var effective = EffectiveDbContext;
        var transaction = await effective.Database.BeginTransactionAsync(cancellationToken);
        return new EfCoreDataBaseTransaction(transaction, effective);
    }

    public IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class
    {
        return new EfCoreQueryableBuilder<TEntity>(EffectiveDbContext.Set<TEntity>(), _chainNode);
    }

    public Task<int> ExecuteSqlRawAsync(
        string sql,
        IEnumerable<object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return EffectiveDbContext.Database.ExecuteSqlRawAsync(
            sql, parameters ?? Array.Empty<object>(), cancellationToken);
    }

    public object GetNativeContext()
    {
        return EffectiveDbContext;
    }

    public void Dispose()
    {
        // DbContext 的生命周期由 DI 容器管理，这里不释放底层实例。
    }

    /// <summary>
    /// requiresNew 隔离期间返回环境推入的内层上下文（链校验 + 身份校验后）；否则返回本作用域上下文。
    /// </summary>
    private DbContext EffectiveDbContext =>
        EfCoreAmbientContext.Resolve(this, _dbContext, _chainNode, _currentTenant?.Id);
}
