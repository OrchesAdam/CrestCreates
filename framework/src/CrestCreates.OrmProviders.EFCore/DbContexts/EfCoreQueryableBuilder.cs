using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using Microsoft.EntityFrameworkCore;
using CrestCreates.OrmProviders.Abstract.Abstractions;

namespace CrestCreates.OrmProviders.EFCore.DbContexts
{
    public class EfCoreQueryableBuilder<TEntity> : IQueryableBuilder<TEntity> where TEntity : class
    {
        private IQueryable<TEntity> _queryable;

        public EfCoreQueryableBuilder(DbSet<TEntity> dbSet)
        {
            _queryable = dbSet.AsQueryable();
        }

        public EfCoreQueryableBuilder(IQueryable<TEntity> queryable)
        {
            _queryable = queryable;
        }

        public IQueryableBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
        {
            _queryable = _queryable.Where(predicate);
            return this;
        }

        public IQueryableBuilder<TEntity> WhereIf(bool condition, Expression<Func<TEntity, bool>> predicate)
        {
            if (condition)
            {
                _queryable = _queryable.Where(predicate);
            }
            return this;
        }

        public IQueryableBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            _queryable = _queryable.OrderBy(keySelector);
            return this;
        }

        public IQueryableBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            _queryable = _queryable.OrderByDescending(keySelector);
            return this;
        }

        public IQueryableBuilder<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            if (_queryable is IOrderedQueryable<TEntity> orderedQueryable)
            {
                _queryable = orderedQueryable.ThenBy(keySelector);
            }
            return this;
        }

        public IQueryableBuilder<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            if (_queryable is IOrderedQueryable<TEntity> orderedQueryable)
            {
                _queryable = orderedQueryable.ThenByDescending(keySelector);
            }
            return this;
        }

        public IQueryableBuilder<TEntity> Skip(int count)
        {
            _queryable = _queryable.Skip(count);
            return this;
        }

        public IQueryableBuilder<TEntity> Take(int count)
        {
            _queryable = _queryable.Take(count);
            return this;
        }

        public IQueryableBuilder<TEntity> Page(int pageIndex, int pageSize)
        {
            _queryable = _queryable.Skip(pageIndex * pageSize).Take(pageSize);
            return this;
        }

        public IQueryableBuilder<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigationPropertyPath)
        {
            if (_queryable is DbSet<TEntity> dbSet)
            {
                _queryable = dbSet.Include(navigationPropertyPath);
            }
            return this;
        }

        public IQueryableBuilder<TEntity> Include(string navigationPropertyPath)
        {
            if (_queryable is DbSet<TEntity> dbSet)
            {
                _queryable = dbSet.Include(navigationPropertyPath);
            }
            return this;
        }

        public IQueryableBuilder<TEntity> ThenInclude<TPreviousProperty, TProperty>(Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath)
        {
            // This is a simplified implementation
            // In practice, this would require more complex handling
            return this;
        }

        public IQueryableBuilder<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) where TResult : class
        {
            var newQueryable = _queryable.Select(selector);
            return new EfCoreQueryableBuilder<TResult>(newQueryable);
        }

        public IQueryableBuilder<TEntity> Distinct()
        {
            _queryable = _queryable.Distinct();
            return this;
        }

        public async Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.ToListAsync(cancellationToken);
        }

        public async Task<TEntity> FirstAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.FirstAsync(cancellationToken);
        }

        public async Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<TEntity> SingleAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.SingleAsync(cancellationToken);
        }

        public async Task<TEntity> SingleOrDefaultAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.SingleOrDefaultAsync(cancellationToken);
        }

        public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.AnyAsync(cancellationToken);
        }

        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _queryable.AnyAsync(predicate, cancellationToken);
        }

        public async Task<int> CountAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.CountAsync(cancellationToken);
        }

        public async Task<long> LongCountAsync(CancellationToken cancellationToken = default)
        {
            return await _queryable.LongCountAsync(cancellationToken);
        }

        public async Task<PagedResult<TEntity>> ToPagedResultAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
        {
            var totalCount = await _queryable.LongCountAsync(cancellationToken);
            var items = await _queryable.Skip(pageIndex * pageSize).Take(pageSize).ToListAsync(cancellationToken);

            return new PagedResult<TEntity>
            {
                Items = items,
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        public IQueryableBuilder<TEntity> AsNoTracking()
        {
            _queryable = _queryable.AsNoTracking();
            return this;
        }

        public IQueryableBuilder<TEntity> IgnoreQueryFilters()
        {
            _queryable = _queryable.IgnoreQueryFilters();
            return this;
        }

        public object GetNativeQuery() => _queryable;
    }
}