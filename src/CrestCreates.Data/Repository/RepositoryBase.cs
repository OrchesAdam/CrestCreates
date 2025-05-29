using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Data.Context;

namespace CrestCreates.Data.Repository
{
    /// <summary>
    /// 基础仓储抽象实现
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public abstract class RepositoryBase<TEntity> : IRepository<TEntity> where TEntity : class
    {
        protected readonly IDbContext DbContext;
        protected readonly IDbSet<TEntity> DbSet;

        protected RepositoryBase(IDbContext dbContext)
        {
            DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            DbSet = dbContext.Set<TEntity>();
        }

        #region 查询操作

        public virtual async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
        {
            return await DbSet.FindAsync(cancellationToken, id);
        }

        public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            var query = DbSet.AsQueryable();
            if (predicate != null)
            {
                query = query.Where(predicate);
            }
            return await Task.FromResult(query.FirstOrDefault());
        }

        public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(DbSet.AsQueryable().ToList());
        }

        public virtual async Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var query = DbSet.AsQueryable().Where(predicate);
            return await Task.FromResult(query.ToList());
        }

        public virtual async Task<IPagedResult<TEntity>> GetPagedAsync(int pageIndex, int pageSize,
            Expression<Func<TEntity, bool>>? predicate = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            CancellationToken cancellationToken = default)
        {
            var query = DbSet.AsQueryable();

            if (predicate != null)
            {
                query = query.Where(predicate);
            }

            var totalCount = query.Count();

            if (orderBy != null)
            {
                query = orderBy(query);
            }

            var items = query.Skip(pageIndex * pageSize).Take(pageSize).ToList();

            return await Task.FromResult(new PagedResult<TEntity>(items, pageIndex, pageSize, totalCount));
        }

        public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var query = DbSet.AsQueryable().Where(predicate);
            return await Task.FromResult(query.Any());
        }

        public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            var query = DbSet.AsQueryable();
            if (predicate != null)
            {
                query = query.Where(predicate);
            }
            return await Task.FromResult(query.Count());
        }

        #endregion

        #region 写入操作

        public virtual async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            DbSet.Add(entity);
            return await Task.FromResult(entity);
        }

        public virtual async Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities,
            CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            DbSet.AddRange(entityList);
            return await Task.FromResult(entityList);
        }

        public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            DbSet.Update(entity);
            return await Task.FromResult(entity);
        }

        public virtual async Task<IEnumerable<TEntity>> UpdateRangeAsync(IEnumerable<TEntity> entities,
            CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            DbSet.UpdateRange(entityList);
            return await Task.FromResult(entityList);
        }

        public virtual async Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            DbSet.Remove(entity);
            return await Task.FromResult(true);
        }

        public virtual async Task<bool> DeleteAsync(object id, CancellationToken cancellationToken = default)
        {
            var entity = await GetByIdAsync(id, cancellationToken);
            if (entity != null)
            {
                DbSet.Remove(entity);
                return true;
            }
            return false;
        }

        public virtual async Task<int> DeleteAsync(Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var entities = await GetAsync(predicate, cancellationToken);
            var entityList = entities.ToList();
            DbSet.RemoveRange(entityList);
            return entityList.Count;
        }

        public virtual async Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities,
            CancellationToken cancellationToken = default)
        {
            var entityList = entities.ToList();
            DbSet.RemoveRange(entityList);
            return await Task.FromResult(entityList.Count);
        }

        #endregion

        #region 查询构建器

        public virtual IQueryable<TEntity> Query()
        {
            return DbSet.AsQueryable();
        }

        public virtual IQueryable<TEntity> Query(Expression<Func<TEntity, bool>> predicate)
        {
            return DbSet.AsQueryable().Where(predicate);
        }

        #endregion
    }
}
