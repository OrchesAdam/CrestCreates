using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.Data.Context;

namespace CrestCreates.Repositories.EFCore
{
    /// <summary>
    /// Entity Framework Core 数据库集合实现
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public class EFCoreDbSet<TEntity> : IDbSet<TEntity> where TEntity : class
    {
        private readonly DbSet<TEntity> _dbSet;

        public EFCoreDbSet(DbSet<TEntity> dbSet)
        {
            _dbSet = dbSet ?? throw new ArgumentNullException(nameof(dbSet));
        }

        public void Add(TEntity entity)
        {
            _dbSet.Add(entity);
        }

        public void AddRange(IEnumerable<TEntity> entities)
        {
            _dbSet.AddRange(entities);
        }

        public void Update(TEntity entity)
        {
            _dbSet.Update(entity);
        }

        public void UpdateRange(IEnumerable<TEntity> entities)
        {
            _dbSet.UpdateRange(entities);
        }

        public void Remove(TEntity entity)
        {
            _dbSet.Remove(entity);
        }

        public void RemoveRange(IEnumerable<TEntity> entities)
        {
            _dbSet.RemoveRange(entities);
        }

        public TEntity? Find(params object[] keyValues)
        {
            return _dbSet.Find(keyValues);
        }

        public async Task<TEntity?> FindAsync(params object[] keyValues)
        {
            return await _dbSet.FindAsync(keyValues);
        }

        public async Task<TEntity?> FindAsync(CancellationToken cancellationToken, params object[] keyValues)
        {
            return await _dbSet.FindAsync(keyValues, cancellationToken);
        }

        public IQueryable<TEntity> AsQueryable()
        {
            return _dbSet.AsQueryable();
        }
    }
}
