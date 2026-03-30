using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.Abstract;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.OrmProviders.EFCore.DbContexts;

/// <summary>
/// EF Core 数据库上下文接口
/// </summary>
public interface IEntityFrameworkCoreDbContext : IDataBaseContext
{
    
    /// <summary>
    /// 获取 ORM 提供者类型
    /// </summary>
    OrmProvider Provider { get; }
    
    // 添加ORM特定的扩展方法
    IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class;
    
    // 可以添加更多ORM相关的方法
    Task<int> ExecuteSqlRawAsync(string sql, IEnumerable<object>? parameters = null, CancellationToken cancellationToken = default);
}