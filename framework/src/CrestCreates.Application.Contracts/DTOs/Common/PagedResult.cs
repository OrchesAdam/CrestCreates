using System;
using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// 分页结果基类
/// </summary>
/// <typeparam name="T">数据项类型</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// 数据项列表
    /// </summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// 总记录数
    /// </summary>
    public int TotalCount { get; }

    /// <summary>
    /// 当前页索引（从0开始）
    /// </summary>
    public int PageIndex { get; }

    /// <summary>
    /// 每页大小
    /// </summary>
    public int PageSize { get; }

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

    /// <summary>
    /// 创建分页结果实例
    /// </summary>
    /// <param name="items">数据项列表</param>
    /// <param name="totalCount">总记录数</param>
    /// <param name="pageIndex">当前页索引</param>
    /// <param name="pageSize">每页大小</param>
    public PagedResult(IReadOnlyList<T> items, int totalCount, int pageIndex, int pageSize)
    {
        Items = items ?? new List<T>();
        TotalCount = totalCount;
        PageIndex = pageIndex;
        PageSize = pageSize;
    }

    /// <summary>
    /// 创建空的分页结果
    /// </summary>
    /// <returns>空的分页结果</returns>
    public static PagedResult<T> Empty(int pageIndex = 0, int pageSize = 10)
    {
        return new PagedResult<T>(new List<T>(), 0, pageIndex, pageSize);
    }
}
