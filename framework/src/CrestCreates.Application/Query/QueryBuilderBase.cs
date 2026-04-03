using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Query
{
    /// <summary>
    /// 查询构建器抽象基类
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public abstract class QueryBuilderBase<TEntity> : IQueryBuilder<TEntity>
        where TEntity : class
    {
        protected readonly List<Expression<Func<TEntity, bool>>> Filters = new();
        protected readonly List<(Expression<Func<TEntity, object>> KeySelector, bool Ascending)> Sorts = new();

        protected int PageIndexValue = 0;
        protected int PageSizeValue = 10;
        protected bool IsPaged = false;

        /// <summary>
        /// 添加过滤条件
        /// </summary>
        public virtual IQueryBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate)
        {
            Filters.Add(predicate);
            return this;
        }

        /// <summary>
        /// 添加排序条件
        /// </summary>
        public virtual IQueryBuilder<TEntity> OrderBy<TKey>(
            Expression<Func<TEntity, TKey>> keySelector,
            bool ascending = true)
        {
            Sorts.Add((ConvertToObjectExpression(keySelector), ascending));
            return this;
        }

        /// <summary>
        /// 添加降序排序条件
        /// </summary>
        public virtual IQueryBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector)
        {
            return OrderBy(keySelector, false);
        }

        /// <summary>
        /// 设置分页参数
        /// </summary>
        public virtual IQueryBuilder<TEntity> Page(int pageIndex, int pageSize)
        {
            PageIndexValue = Math.Max(0, pageIndex);
            PageSizeValue = Math.Max(1, pageSize);
            IsPaged = true;
            return this;
        }

        /// <summary>
        /// 执行查询并返回列表
        /// </summary>
        public abstract Task<List<TEntity>> ToListAsync();

        /// <summary>
        /// 执行查询并返回分页结果
        /// </summary>
        public virtual async Task<PagedResult<TEntity>> ToPagedResultAsync()
        {
            var totalCount = await CountAsync();
            var items = await ToListAsync();

            return new PagedResult<TEntity>(
                items,
                totalCount,
                PageIndexValue,
                PageSizeValue
            );
        }

        /// <summary>
        /// 获取符合条件的记录总数
        /// </summary>
        public abstract Task<int> CountAsync();

        /// <summary>
        /// 检查是否存在符合条件的记录
        /// </summary>
        public abstract Task<bool> AnyAsync();

        /// <summary>
        /// 获取第一条记录，如果不存在则返回 null
        /// </summary>
        public abstract Task<TEntity?> FirstOrDefaultAsync();

        /// <summary>
        /// 获取第一条记录
        /// </summary>
        public virtual async Task<TEntity> FirstAsync()
        {
            var entity = await FirstOrDefaultAsync();
            if (entity == null)
            {
                throw new InvalidOperationException("序列不包含任何元素");
            }
            return entity;
        }

        /// <summary>
        /// 将类型化的表达式转换为 object 类型表达式
        /// </summary>
        private static Expression<Func<TEntity, object>> ConvertToObjectExpression<TKey>(
            Expression<Func<TEntity, TKey>> keySelector)
        {
            if (keySelector.Body is MemberExpression memberExpression)
            {
                return Expression.Lambda<Func<TEntity, object>>(
                    Expression.Convert(memberExpression, typeof(object)),
                    keySelector.Parameters);
            }

            return Expression.Lambda<Func<TEntity, object>>(
                Expression.Convert(keySelector.Body, typeof(object)),
                keySelector.Parameters);
        }

        /// <summary>
        /// 应用过滤条件到查询
        /// </summary>
        protected IQueryable<TEntity> ApplyFilters(IQueryable<TEntity> query)
        {
            foreach (var filter in Filters)
            {
                query = query.Where(filter);
            }
            return query;
        }

        /// <summary>
        /// 应用排序条件到查询
        /// </summary>
        protected IQueryable<TEntity> ApplySorting(IQueryable<TEntity> query)
        {
            if (Sorts.Count == 0)
            {
                return query;
            }

            var firstSort = Sorts[0];
            var orderedQuery = firstSort.Ascending
                ? query.OrderBy(firstSort.KeySelector)
                : query.OrderByDescending(firstSort.KeySelector);

            for (int i = 1; i < Sorts.Count; i++)
            {
                var sort = Sorts[i];
                orderedQuery = sort.Ascending
                    ? orderedQuery.ThenBy(sort.KeySelector)
                    : orderedQuery.ThenByDescending(sort.KeySelector);
            }

            return orderedQuery;
        }

        /// <summary>
        /// 应用分页到查询
        /// </summary>
        protected IQueryable<TEntity> ApplyPaging(IQueryable<TEntity> query)
        {
            if (!IsPaged)
            {
                return query;
            }

            return query
                .Skip(PageIndexValue * PageSizeValue)
                .Take(PageSizeValue);
        }

        /// <summary>
        /// 重置查询构建器状态
        /// </summary>
        protected virtual void Reset()
        {
            Filters.Clear();
            Sorts.Clear();
            PageIndexValue = 0;
            PageSizeValue = 10;
            IsPaged = false;
        }
    }
}
