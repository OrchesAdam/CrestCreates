using System;
using System.Collections.Generic;

namespace CrestCreates.Domain.Shared.DTOs;

public class PagedResult<TEntity>
{
    private int _pageIndex;
    private int _pageSize = 10;

    public IReadOnlyList<TEntity> Items { get; set; } = new List<TEntity>();
    public int TotalCount { get; set; }
    public int PageIndex
    {
        get => _pageIndex;
        set => _pageIndex = value < 0 ? 0 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 10 : value;
    }

    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageIndex > 0;
    public bool HasNextPage => PageIndex < TotalPages - 1;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<TEntity> items, int totalCount, int pageIndex, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageIndex = pageIndex;
        PageSize = pageSize;
    }
}
