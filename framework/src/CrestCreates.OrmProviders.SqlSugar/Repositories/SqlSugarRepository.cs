using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using SqlSugar;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.OrmProviders.SqlSugar.Repositories
{
    public abstract class SqlSugarRepository<TEntity, TKey> : IRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>, new()
        where TKey : IEquatable<TKey>
    {
        protected readonly ISqlSugarClient _sqlSugarClient;

        protected SqlSugarRepository(ISqlSugarClient sqlSugarClient)
        {
            _sqlSugarClient = sqlSugarClient;
        }

        public virtual async Task<TEntity> GetByIdAsync(TKey id)
        {
            return await _sqlSugarClient.Queryable<TEntity>()
                .InSingleAsync(id);
        }

        public virtual async Task<List<TEntity>> GetAllAsync()
        {
            return await _sqlSugarClient.Queryable<TEntity>()
                .ToListAsync();
        }

        public virtual async Task<TEntity> AddAsync(TEntity entity)
        {
            await _sqlSugarClient.Insertable(entity).ExecuteCommandAsync();
            return entity;
        }

        public virtual async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _sqlSugarClient.Queryable<TEntity>()
                .Where(predicate)
                .ToListAsync();
        }

        public virtual async Task<TEntity> UpdateAsync(TEntity entity)
        {
            await _sqlSugarClient.Updateable(entity).ExecuteCommandAsync();
            return entity;
        }

        public virtual async Task DeleteAsync(TEntity entity)
        {
            await _sqlSugarClient.Deleteable(entity)
                .ExecuteCommandAsync();
        }
    }
}