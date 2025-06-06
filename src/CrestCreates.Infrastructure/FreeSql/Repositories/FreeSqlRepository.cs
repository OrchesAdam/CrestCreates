using FreeSql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Infrastructure.FreeSql.Repositories
{
    public abstract class FreeSqlRepository<TEntity, TKey> : IRepository<TEntity, TKey>
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>
    {
        private readonly IFreeSql _freeSql;

        public FreeSqlRepository(IFreeSql freeSql)
        {
            _freeSql = freeSql;
        }

        public async Task<TEntity> GetByIdAsync(TKey id)
        {
            return await _freeSql.Select<TEntity>().Where(e => e.Id.Equals(id)).FirstAsync();
        }

        public async Task<List<TEntity>> GetAllAsync()
        {
            return await _freeSql.Select<TEntity>().ToListAsync();
        }

        public async Task<TEntity> AddAsync(TEntity entity)
        {
            await _freeSql.Insert(entity).ExecuteAffrowsAsync();
            return entity;
        }

        public async Task<TEntity> UpdateAsync(TEntity entity)
        {
            await _freeSql.Update<TEntity>(entity).ExecuteAffrowsAsync();
            return entity;
        }

        public async Task DeleteAsync(TEntity entity)
        {
            await _freeSql.Delete<TEntity>(entity).ExecuteAffrowsAsync();
        }

        public async Task<List<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _freeSql.Select<TEntity>().Where(predicate).ToListAsync();
        }
    }
}