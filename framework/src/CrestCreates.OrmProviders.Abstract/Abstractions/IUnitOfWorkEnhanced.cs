using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Entities;

namespace CrestCreates.OrmProviders.Abstract.Abstractions
{
    /// <summary>
    /// 增强的工作单元接口
    /// </summary>
    /// <remarks>
    /// 扩展了 Domain.UnitOfWork.IUnitOfWork，提供更丰富的功能
    /// 包括仓储访问、数据库上下文访问、事务管理等
    /// </remarks>
    public interface IUnitOfWorkEnhanced : Domain.UnitOfWork.IUnitOfWork
    {
        #region 数据库上下文

        /// <summary>
        /// 获取数据库上下文
        /// </summary>
        IDataBaseContext DataBaseContext { get; }

        /// <summary>
        /// 获取 ORM 提供者类型
        /// </summary>
        OrmProvider Provider { get; }

        #endregion

        #region 仓储访问

        /// <summary>
        /// 获取指定实体类型的仓储
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>仓储实例</returns>
        IRepository<TEntity> GetRepository<TEntity>() where TEntity : class;

        /// <summary>
        /// 获取指定实体类型的仓储（带主键）
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <typeparam name="TKey">主键类型</typeparam>
        /// <returns>仓储实例</returns>
        IRepository<TEntity, TKey> GetRepository<TEntity, TKey>() 
            where TEntity : class, IEntity<TKey> where TKey : IEquatable<TKey>;

        /// <summary>
        /// 获取指定实体类型的只读仓储
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <returns>只读仓储实例</returns>
        IReadOnlyRepository<TEntity> GetReadOnlyRepository<TEntity>() where TEntity : class;

        #endregion

        #region 事务管理（扩展）

        /// <summary>
        /// 使用指定隔离级别开始事务
        /// </summary>
        /// <param name="isolationLevel">事务隔离级别</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取当前活动事务
        /// </summary>
        IDbTransaction CurrentTransaction { get; }

        /// <summary>
        /// 是否存在活动事务
        /// </summary>
        bool HasActiveTransaction { get; }

        #endregion

        #region 高级功能

        /// <summary>
        /// 启用软删除过滤器
        /// </summary>
        void EnableSoftDeleteFilter();

        /// <summary>
        /// 禁用软删除过滤器
        /// </summary>
        void DisableSoftDeleteFilter();

        /// <summary>
        /// 启用多租户过滤器
        /// </summary>
        void EnableMultiTenancyFilter();

        /// <summary>
        /// 禁用多租户过滤器
        /// </summary>
        void DisableMultiTenancyFilter();

        /// <summary>
        /// 设置当前租户ID
        /// </summary>
        /// <param name="tenantId">租户ID</param>
        void SetTenantId(Guid? tenantId);

        /// <summary>
        /// 获取当前租户ID
        /// </summary>
        Guid? GetTenantId();

        #endregion
    }

    /// <summary>
    /// 工作单元选项
    /// </summary>
    public class UnitOfWorkOptions
    {
        /// <summary>
        /// 事务隔离级别
        /// </summary>
        public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

        /// <summary>
        /// 是否自动开始事务
        /// </summary>
        public bool IsTransactional { get; set; } = true;

        /// <summary>
        /// 超时时间（秒）
        /// </summary>
        public int? Timeout { get; set; }

        /// <summary>
        /// 是否启用软删除过滤器
        /// </summary>
        public bool EnableSoftDeleteFilter { get; set; } = true;

        /// <summary>
        /// 是否启用多租户过滤器
        /// </summary>
        public bool EnableMultiTenancyFilter { get; set; } = true;

        /// <summary>
        /// ORM 提供者
        /// </summary>
        public OrmProvider? Provider { get; set; }
    }
}
