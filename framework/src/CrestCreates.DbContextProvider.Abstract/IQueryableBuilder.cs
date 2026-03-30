using System.Linq.Expressions;

namespace CrestCreates.DbContextProvider.Abstract
{
    /// <summary>
    /// 查询构建器统一抽象接口
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <remarks>
    /// 提供跨 ORM 的统一查询接口，支持过滤、排序、分页等操作
    /// 采用流式 API 设计，支持链式调用
    /// </remarks>
    public interface IQueryableBuilder<TEntity> where TEntity : class
    {
        #region 过滤条件

        /// <summary>
        /// 添加 Where 条件
        /// </summary>
        /// <param name="predicate">条件表达式</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Where(Expression<Func<TEntity, bool>> predicate);

        /// <summary>
        /// 添加条件性 Where 条件
        /// </summary>
        /// <param name="condition">是否应用条件</param>
        /// <param name="predicate">条件表达式</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> WhereIf(bool condition, Expression<Func<TEntity, bool>> predicate);

        #endregion

        #region 排序

        /// <summary>
        /// 升序排序
        /// </summary>
        /// <typeparam name="TKey">排序字段类型</typeparam>
        /// <param name="keySelector">排序字段选择器</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        /// <summary>
        /// 降序排序
        /// </summary>
        /// <typeparam name="TKey">排序字段类型</typeparam>
        /// <param name="keySelector">排序字段选择器</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        /// <summary>
        /// 次级升序排序
        /// </summary>
        /// <typeparam name="TKey">排序字段类型</typeparam>
        /// <param name="keySelector">排序字段选择器</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> ThenBy<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        /// <summary>
        /// 次级降序排序
        /// </summary>
        /// <typeparam name="TKey">排序字段类型</typeparam>
        /// <param name="keySelector">排序字段选择器</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> ThenByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector);

        #endregion

        #region 分页

        /// <summary>
        /// 跳过指定数量的记录
        /// </summary>
        /// <param name="count">跳过的记录数</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Skip(int count);

        /// <summary>
        /// 获取指定数量的记录
        /// </summary>
        /// <param name="count">获取的记录数</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Take(int count);

        /// <summary>
        /// 分页查询
        /// </summary>
        /// <param name="pageIndex">页码（从 0 开始）</param>
        /// <param name="pageSize">每页记录数</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Page(int pageIndex, int pageSize);

        #endregion

        #region 关联查询

        /// <summary>
        /// 包含导航属性（贪婪加载）
        /// </summary>
        /// <typeparam name="TProperty">导航属性类型</typeparam>
        /// <param name="navigationPropertyPath">导航属性路径</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigationPropertyPath);

        /// <summary>
        /// 包含导航属性（字符串路径）
        /// </summary>
        /// <param name="navigationPropertyPath">导航属性路径字符串</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Include(string navigationPropertyPath);

        /// <summary>
        /// 然后包含导航属性（用于多级导航）
        /// </summary>
        /// <typeparam name="TPreviousProperty">上一级导航属性类型</typeparam>
        /// <typeparam name="TProperty">当前导航属性类型</typeparam>
        /// <param name="navigationPropertyPath">导航属性路径</param>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> ThenInclude<TPreviousProperty, TProperty>(
            Expression<Func<TPreviousProperty, TProperty>> navigationPropertyPath);

        #endregion

        #region 投影

        /// <summary>
        /// 选择特定字段
        /// </summary>
        /// <typeparam name="TResult">结果类型</typeparam>
        /// <param name="selector">字段选择器</param>
        /// <returns>新的查询构建器</returns>
        IQueryableBuilder<TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) where TResult : class;

        #endregion

        #region 聚合

        /// <summary>
        /// 去重
        /// </summary>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> Distinct();

        #endregion

        #region 执行查询

        /// <summary>
        /// 转换为列表
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>实体列表</returns>
        Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取第一个元素
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>第一个实体，如果不存在则抛出异常</returns>
        Task<TEntity> FirstAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取第一个元素或默认值
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>第一个实体，如果不存在则返回 null</returns>
        Task<TEntity> FirstOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取单个元素
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>单个实体，如果有多个或不存在则抛出异常</returns>
        Task<TEntity> SingleAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取单个元素或默认值
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>单个实体，如果不存在则返回 null，如果有多个则抛出异常</returns>
        Task<TEntity> SingleOrDefaultAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否存在任何元素
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>如果存在则返回 true</returns>
        Task<bool> AnyAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查是否存在满足条件的元素
        /// </summary>
        /// <param name="predicate">条件表达式</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>如果存在则返回 true</returns>
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取元素数量
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>元素数量</returns>
        Task<int> CountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取元素长整型数量
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>元素数量</returns>
        Task<long> LongCountAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 分页查询（返回分页结果）
        /// </summary>
        /// <param name="pageIndex">页码（从 0 开始）</param>
        /// <param name="pageSize">每页记录数</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>分页结果</returns>
        Task<PagedResult<TEntity>> ToPagedResultAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);

        #endregion

        #region 高级功能

        /// <summary>
        /// 禁用查询跟踪（仅查询，不跟踪实体状态）
        /// </summary>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> AsNoTracking();

        /// <summary>
        /// 禁用软删除过滤器
        /// </summary>
        /// <returns>查询构建器</returns>
        IQueryableBuilder<TEntity> IgnoreQueryFilters();

        /// <summary>
        /// 获取原生查询对象
        /// </summary>
        /// <remarks>
        /// 用于访问特定 ORM 的原生查询功能
        /// EF Core: IQueryable<TEntity>
        /// FreeSql: ISelect<TEntity>
        /// SqlSugar: ISugarQueryable<TEntity>
        /// </remarks>
        object GetNativeQuery();

        #endregion
    }

    /// <summary>
    /// 分页结果
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// 数据列表
        /// </summary>
        public List<T> Items { get; set; }

        /// <summary>
        /// 总记录数
        /// </summary>
        public long TotalCount { get; set; }

        /// <summary>
        /// 当前页码（从 0 开始）
        /// </summary>
        public int PageIndex { get; set; }

        /// <summary>
        /// 每页记录数
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

        /// <summary>
        /// 是否有上一页
        /// </summary>
        public bool HasPreviousPage => PageIndex > 0;

        /// <summary>
        /// 是否有下一页
        /// </summary>
        public bool HasNextPage => PageIndex + 1 < TotalPages;
    }
}
