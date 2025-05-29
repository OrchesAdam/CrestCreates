using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Data.Repository;

namespace CrestCreates.Repositories.EFCore
{
    /// <summary>
    /// Entity Framework Core 仓储实现
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public class EFCoreRepository<TEntity> : RepositoryBase<TEntity> where TEntity : class
    {
        private readonly EFCoreDbContext _efCoreContext;
        private readonly DbSet<TEntity> _efDbSet;        public EFCoreRepository(EFCoreDbContext dbContext) : base(dbContext)
        {
            _efCoreContext = dbContext;
            _efDbSet = ((DbContext)_efCoreContext).Set<TEntity>();
        }

        #region 重写基类方法以优化 EF Core 性能

        public override async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            return await _efDbSet.FindAsync(new[] { id }, cancellationToken);
        }

        public override async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                return await _efDbSet.FirstOrDefaultAsync(cancellationToken);
            }
            return await _efDbSet.FirstOrDefaultAsync(predicate, cancellationToken);
        }

        public override async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _efDbSet.ToListAsync(cancellationToken);
        }

        public override async Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return await _efDbSet.Where(predicate).ToListAsync(cancellationToken);
        }        public override async Task<IPagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize,
            Expression<Func<TEntity, bool>>? predicate = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            CancellationToken cancellationToken = default)
        {
            var query = _efDbSet.AsQueryable();
            
            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            var totalCount = await query.CountAsync(cancellationToken);
            
            if (orderBy != null)
            {
                query = orderBy(query);
            }

            var items = await query
                .Skip(pageIndex * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<TEntity>(items, pageIndex, pageSize, totalCount);
        }        public override async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                return await _efDbSet.CountAsync(cancellationToken);
            }
            return await _efDbSet.CountAsync(predicate, cancellationToken);
        }

        public override async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            if (predicate == null)
            {
                return await _efDbSet.AnyAsync(cancellationToken);
            }
            return await _efDbSet.AnyAsync(predicate, cancellationToken);
        }

        #endregion

        #region EF Core 特定功能

        /// <summary>
        /// 包含相关实体的查询
        /// </summary>
        /// <param name="includeProperties">要包含的导航属性</param>
        /// <returns>可查询对象</returns>
        public IQueryable<TEntity> Include(params Expression<Func<TEntity, object>>[] includeProperties)
        {
            var query = _efDbSet.AsQueryable();
            
            foreach (var includeProperty in includeProperties)
            {
                query = query.Include(includeProperty);
            }
            
            return query;
        }

        /// <summary>
        /// 包含相关实体的查询（字符串方式）
        /// </summary>
        /// <param name="includeProperties">要包含的导航属性名称</param>
        /// <returns>可查询对象</returns>
        public IQueryable<TEntity> Include(params string[] includeProperties)
        {
            var query = _efDbSet.AsQueryable();
            
            foreach (var includeProperty in includeProperties)
            {
                query = query.Include(includeProperty);
            }
            
            return query;
        }

        /// <summary>
        /// 无跟踪查询
        /// </summary>
        /// <returns>可查询对象</returns>
        public IQueryable<TEntity> AsNoTracking()
        {
            return _efDbSet.AsNoTracking();
        }

        /// <summary>
        /// 原始 SQL 查询
        /// </summary>
        /// <param name="sql">SQL 语句</param>
        /// <param name="parameters">参数</param>
        /// <returns>可查询对象</returns>
        public IQueryable<TEntity> FromSqlRaw(string sql, params object[] parameters)
        {
            return _efDbSet.FromSqlRaw(sql, parameters);
        }

        /// <summary>
        /// 批量删除
        /// </summary>
        /// <param name="predicate">删除条件</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> BatchDeleteAsync(Expression<Func<TEntity, bool>> predicate, 
            CancellationToken cancellationToken = default)
        {
            // EF Core 7+ 支持 ExecuteDeleteAsync
            // 对于较早版本，需要先查询后删除
            var entities = await _efDbSet.Where(predicate).ToListAsync(cancellationToken);
            _efDbSet.RemoveRange(entities);
            return entities.Count;
        }

        /// <summary>
        /// 批量更新
        /// </summary>
        /// <param name="predicate">更新条件</param>
        /// <param name="updateExpression">更新表达式</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>受影响的行数</returns>
        public async Task<int> BatchUpdateAsync(Expression<Func<TEntity, bool>> predicate,
            Expression<Func<TEntity, TEntity>> updateExpression, 
            CancellationToken cancellationToken = default)
        {
            // EF Core 7+ 支持 ExecuteUpdateAsync
            // 对于较早版本，需要先查询后更新
            var entities = await _efDbSet.Where(predicate).ToListAsync(cancellationToken);
            
            // 这里需要根据 updateExpression 来更新实体
            // 简化实现，实际使用时可能需要更复杂的表达式解析
            foreach (var entity in entities)
            {
                var updatedEntity = updateExpression.Compile()(entity);
                _efCoreContext.Entry(entity).CurrentValues.SetValues(updatedEntity);
            }
            
            return entities.Count;
        }

        #endregion
    }
}
