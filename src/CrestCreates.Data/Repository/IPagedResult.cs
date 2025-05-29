using System.Collections.Generic;

namespace CrestCreates.Data.Repository
{
    /// <summary>
    /// 分页结果接口
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    public interface IPagedResult<T>
    {
        /// <summary>
        /// 当前页数据
        /// </summary>
        IEnumerable<T> Items { get; }

        /// <summary>
        /// 页索引（从0开始）
        /// </summary>
        int PageIndex { get; }

        /// <summary>
        /// 页大小
        /// </summary>
        int PageSize { get; }

        /// <summary>
        /// 总记录数
        /// </summary>
        int TotalCount { get; }

        /// <summary>
        /// 总页数
        /// </summary>
        int TotalPages { get; }

        /// <summary>
        /// 是否有上一页
        /// </summary>
        bool HasPreviousPage { get; }

        /// <summary>
        /// 是否有下一页
        /// </summary>
        bool HasNextPage { get; }
    }

    /// <summary>
    /// 分页结果实现
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    public class PagedResult<T> : IPagedResult<T>
    {
        public IEnumerable<T> Items { get; }
        public int PageIndex { get; }
        public int PageSize { get; }
        public int TotalCount { get; }
        public int TotalPages { get; }
        public bool HasPreviousPage => PageIndex > 0;
        public bool HasNextPage => PageIndex + 1 < TotalPages;

        public PagedResult(IEnumerable<T> items, int pageIndex, int pageSize, int totalCount)
        {
            Items = items;
            PageIndex = pageIndex;
            PageSize = pageSize;
            TotalCount = totalCount;
            TotalPages = (int)System.Math.Ceiling(totalCount / (double)pageSize);
        }

        /// <summary>
        /// 创建空的分页结果
        /// </summary>
        /// <param name="pageIndex">页索引</param>
        /// <param name="pageSize">页大小</param>
        /// <returns>空分页结果</returns>
        public static PagedResult<T> Empty(int pageIndex, int pageSize)
        {
            return new PagedResult<T>(new List<T>(), pageIndex, pageSize, 0);
        }
    }
}
