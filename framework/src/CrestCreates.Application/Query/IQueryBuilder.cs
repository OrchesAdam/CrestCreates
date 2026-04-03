using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Query
{
    /// <summary>
    /// 查询构建器泛型接口
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    public interface IQueryBuilder<TEntity> where TEntity : class
    {
        /// <summary>
        /// 添加过滤条件
        /// </summary>
        /// <param name="predicate">过滤表达式</param>
        /// <returns>查询构建器实例（支持链式调用）</returns>
        IQueryBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// 添加排序条件
        /// </summary>
        /// <typeparam name="TKey">排序键类型</typeparam>
        /// <param name="keySelector">排序键选择器</param>
        /// <param name="ascending">是否升序</param>
        /// <returns>查询构建器实例（支持链式调用）</returns>
        IQueryBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector, bool ascending = true);

        /// <summary>
        /// 添加降序排序条件
        /// </summary>
        /// <typeparam name="TKey">排序键类型</typeparam>
        /// <param name="keySelector">排序键选择器</param>
        /// <returns>查询构建器实例（支持链式调用）</returns>
        IQueryBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        /// <summary>
        /// 设置分页参数
        /// </summary>
        /// <param name="pageIndex">页索引（从0开始）</param>
        /// <param name="pageSize">每页大小</param>
        /// <returns>查询构建器实例（支持链式调用）</returns>
        IQueryBuilder<TEntity> Page(int pageIndex, int pageSize);

        /// <summary>
        /// 执行查询并返回列表
        /// </summary>
        /// <returns>实体列表</returns>
        Task<List<TEntity>> ToListAsync();

        /// <summary>
        /// 执行查询并返回分页结果
        /// </summary>
        /// <returns>分页结果</returns>
        Task<PagedResult<TEntity>> ToPagedResultAsync();

        /// <summary>
        /// 获取符合条件的记录总数
        /// </summary>
        /// <returns>记录总数</returns>
        Task<int> CountAsync();

        /// <summary>
        /// 检查是否存在符合条件的记录
        /// </summary>
        /// <returns>是否存在</returns>
        Task<bool> AnyAsync();

        /// <summary>
        /// 获取第一条记录
        /// </summary>
        /// <returns>第一条记录，如果不存在则返回 null</returns>
        Task<TEntity?> FirstOrDefaultAsync();

        /// <summary>
        /// 获取第一条记录
        /// </summary>
        /// <returns>第一条记录</returns>
        Task<TEntity> FirstAsync();
    }
}
