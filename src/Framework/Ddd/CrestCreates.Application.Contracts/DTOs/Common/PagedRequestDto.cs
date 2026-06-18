using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// 分页请求基类
/// </summary>
public class PagedRequestDto
{
    /// <summary>
    /// 最大页大小限制
    /// </summary>
    public const int MaxPageSize = 1000;

    private int _pageIndex = 0;
    private int _pageSize = 10;

    /// <summary>
    /// 当前页索引（从0开始）
    /// </summary>
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = value < 0 ? 0 : value;
    }

    /// <summary>
    /// 每页大小
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 10 : (value > MaxPageSize ? MaxPageSize : value);
    }

    /// <summary>
    /// 排序描述符列表
    /// </summary>
    public List<SortDescriptor>? Sorts { get; set; }

    /// <summary>
    /// 过滤描述符列表
    /// </summary>
    public List<FilterDescriptor>? Filters { get; set; }

    /// <summary>
    /// 获取跳过的记录数
    /// </summary>
    /// <returns>跳过的记录数</returns>
    public int GetSkipCount()
    {
        return PageIndex * PageSize;
    }
}
