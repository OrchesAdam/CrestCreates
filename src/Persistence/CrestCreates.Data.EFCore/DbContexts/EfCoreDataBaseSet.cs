using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.Abstractions;

namespace CrestCreates.Data.EFCore.DbContexts
{
    /// <summary>
    /// EF Core 实体集：数据动作把「传入 CT」与「读方可见的受管执行 token」组合为有效 token
    /// （操作层 token 组合 + 受管链校验，见设计 §4.1 R2-S137-03）。
    /// </summary>
    public class EfCoreDataBaseSet<TEntity> : IDataBaseSet<TEntity> where TEntity : class
    {
        private readonly DbSet<TEntity> _dbSet;
        private readonly UnitOfWorkChainNode? _chainNode;

        public EfCoreDataBaseSet(DbSet<TEntity> dbSet, UnitOfWorkChainNode? chainNode = null)
        {
            _dbSet = dbSet;
            _chainNode = chainNode;
        }

        public async Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, _chainNode, out var effective);
            var result = await _dbSet.AddAsync(entity, effective);
            return result.Entity;
        }

        public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, _chainNode, out var effective);
            await _dbSet.AddRangeAsync(entities, effective);
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
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, _chainNode, out var effective);
            var enumerable = await _dbSet.Where(predicate).ToListAsync(effective);
            return await RemoveRangeAsync(enumerable, effective);
        }

        public async Task<TEntity?> FindAsync(params object[] keyValues)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(default, _chainNode, out var effective);
            return await _dbSet.FindAsync(keyValues, effective);
        }

        public async Task<TEntity?> FindAsync(CancellationToken cancellationToken, params object[] keyValues)
        {
            using var linked = UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, _chainNode, out var effective);
            return await _dbSet.FindAsync(keyValues, effective);
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
