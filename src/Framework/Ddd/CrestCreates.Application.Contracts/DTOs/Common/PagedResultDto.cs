using System;
using System.Collections.Generic;
using CrestCreates.Domain.Shared.DTOs;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// 分页结果基类
/// </summary>
/// <typeparam name="T">数据项类型</typeparam>
public class PagedResultDto<T> : PagedResult<T>
{

    /// <summary>
    /// 创建分页结果实例
    /// </summary>
    /// <param name="items">数据项列表</param>
    /// <param name="totalCount">总记录数</param>
    /// <param name="pageIndex">当前页索引</param>
    /// <param name="pageSize">每页大小</param>
    public PagedResultDto(IReadOnlyList<T> items, int totalCount, int pageIndex, int pageSize)
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
    public static PagedResultDto<T> Empty(int pageIndex = 0, int pageSize = 10)
    {
        return new PagedResultDto<T>(new List<T>(), 0, pageIndex, pageSize);
    }
}
