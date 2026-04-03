using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.DbContextProvider.Abstract;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class EfCoreDataBaseSet<TEntity> : IDataBaseSet<TEntity> where TEntity : class
    {
        private readonly DbSet<TEntity> _dbSet;

        public EfCoreDataBaseSet(DbSet<TEntity> dbSet)
        {
            _dbSet = dbSet;
        }

        public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            var result = await _dbSet.AddAsync(entity, cancellationToken);
            return result.Entity;
        }

        public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            await _dbSet.AddRangeAsync(entities, cancellationToken);
        }

        public void Update(TEntity entity)
        {
            _dbSet.Update(entity);
        }

        public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _dbSet.Update(entity);
            return entity;
        }

        public void UpdateRange(IEnumerable<TEntity> entities)
        {
            _dbSet.UpdateRange(entities);
        }

        public async Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            var enumerable = entities.ToList();
            _dbSet.UpdateRange(enumerable);
            return enumerable.Count;
        }

        public void Remove(TEntity entity)
        {
            _dbSet.Remove(entity);
        }

        public async Task RemoveAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            _dbSet.Remove(entity);
        }

        public void RemoveRange(IEnumerable<TEntity> entities)
        {
            _dbSet.RemoveRange(entities);
        }

        public async Task<int> RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            _dbSet.RemoveRange(entities);
            return entities.Count();
        }

        public async Task<int> RemoveRangeAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var enumerable = await _dbSet.Where(predicate).ToListAsync(cancellationToken);
            return await RemoveRangeAsync(enumerable, cancellationToken);
        }

        public async Task<TEntity?> FindAsync(params object[] keyValues)
        {
            return await _dbSet.FindAsync(keyValues);
        }

        public async Task<TEntity?> FindAsync(CancellationToken cancellationToken, params object[] keyValues)
        {
            return await _dbSet.FindAsync(keyValues, cancellationToken);
        }

        public void Attach(TEntity entity)
        {
            _dbSet.Attach(entity);
        }

        public void AttachRange(IEnumerable<TEntity> entities)
        {
            _dbSet.AttachRange(entities);
        }
    }
}