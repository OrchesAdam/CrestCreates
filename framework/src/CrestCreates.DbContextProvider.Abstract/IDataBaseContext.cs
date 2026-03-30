using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.DbContextProvider.Abstract
{
    /// <summary>
    /// 数据库上下文统一抽象接口
    /// </summary>
    /// <remarks>
    /// 提供跨 EF Core、FreeSql、SqlSugar 的统一数据库上下文抽象
    /// 支持实体集访问、查询构建、事务管理和数据持久化
    /// </remarks>
    public interface IDataBaseContext : IDisposable
    {
        /// <summary>
        /// 获取指定实体类型的实体集
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>实体集接口</returns>
        IDataBaseSet<TEntity> Set<TEntity>() where TEntity : class;

        /// <summary>
        /// 保存更改到数据库
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 开始数据库事务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>数据库事务接口</returns>
        Task<IDataBaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前活动的数据库事务
        /// </summary>
        IDataBaseTransaction CurrentTransaction { get; }

        /// <summary>
        /// 获取数据库连接字符串
        /// </summary>
        string ConnectionString { get; }
        
        /// <summary>
        /// 获取查询构建器
        /// </summary>
        /// <typeparam name="TEntity"></typeparam>
        /// <returns></returns>
        IQueryableBuilder<TEntity> Queryable<TEntity>() where TEntity : class;

        /// <summary>
        /// 获取原生数据库上下文对象
        /// </summary>
        /// <remarks>
        /// 用于访问特定 ORM 的原生功能
        /// EF Core: DbContext
        /// FreeSql: IFreeSql
        /// SqlSugar: ISqlSugarClient
        /// </remarks>
        object GetNativeContext();
    }
}
