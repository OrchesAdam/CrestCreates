using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Data.Repository;
using CrestCreates.Data.UnitOfWork;

namespace CrestCreates.Repositories.EFCore
{
    /// <summary>
    /// Entity Framework Core 工作单元实现
    /// </summary>
    public class EFCoreUnitOfWork : UnitOfWorkBase
    {
        private readonly EFCoreDbContext _efCoreContext;

        public EFCoreUnitOfWork(EFCoreDbContext dbContext) : base(dbContext)
        {
            _efCoreContext = dbContext;
        }

        protected override IRepository<TEntity> CreateRepository<TEntity>() where TEntity : class
        {
            return new EFCoreRepository<TEntity>(_efCoreContext);
        }

        public override async Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _efCoreContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // 这里可以添加更详细的异常处理
                throw new InvalidOperationException("Failed to save changes to database", ex);
            }
        }

        public override int Commit()
        {
            try
            {
                return _efCoreContext.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                // 这里可以添加更详细的异常处理
                throw new InvalidOperationException("Failed to save changes to database", ex);
            }
        }

        #region EF Core 特定功能

        /// <summary>
        /// 获取 EF Core 上下文
        /// </summary>
        /// <returns>EF Core 数据库上下文</returns>
        public EFCoreDbContext GetEFCoreContext()
        {
            return _efCoreContext;
        }

        /// <summary>
        /// 重新加载实体
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entity">要重新加载的实体</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        public async Task ReloadAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) 
            where TEntity : class
        {
            await _efCoreContext.Entry(entity).ReloadAsync(cancellationToken);
        }

        /// <summary>
        /// 分离实体
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entity">要分离的实体</param>
        public void Detach<TEntity>(TEntity entity) where TEntity : class
        {
            _efCoreContext.Entry(entity).State = EntityState.Detached;
        }

        /// <summary>
        /// 附加实体
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entity">要附加的实体</param>
        public void Attach<TEntity>(TEntity entity) where TEntity : class
        {
            _efCoreContext.Entry(entity).State = EntityState.Unchanged;
        }

        /// <summary>
        /// 清除所有跟踪的实体
        /// </summary>
        public void ClearChangeTracker()
        {
            _efCoreContext.ChangeTracker.Clear();
        }

        /// <summary>
        /// 获取实体状态
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entity">实体</param>
        /// <returns>实体状态</returns>
        public EntityState GetEntityState<TEntity>(TEntity entity) where TEntity : class
        {
            return _efCoreContext.Entry(entity).State;
        }

        /// <summary>
        /// 设置实体状态
        /// </summary>
        /// <typeparam name="TEntity">实体类型</typeparam>
        /// <param name="entity">实体</param>
        /// <param name="state">要设置的状态</param>
        public void SetEntityState<TEntity>(TEntity entity, EntityState state) where TEntity : class
        {
            _efCoreContext.Entry(entity).State = state;
        }

        /// <summary>
        /// 应用数据库迁移
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>Task</returns>
        public async Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            await _efCoreContext.Database.MigrateAsync(cancellationToken);
        }

        /// <summary>
        /// 确保数据库已创建
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>如果数据库被创建则返回 true，否则返回 false</returns>
        public async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            return await _efCoreContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        /// <summary>
        /// 确保数据库已删除
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>如果数据库被删除则返回 true，否则返回 false</returns>
        public async Task<bool> EnsureDeletedAsync(CancellationToken cancellationToken = default)
        {
            return await _efCoreContext.Database.EnsureDeletedAsync(cancellationToken);
        }

        #endregion
    }
}
