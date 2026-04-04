using System;
using System.Collections.Generic;

namespace CrestCreates.Domain.Shared.DTOs;

public class PagedResult<TEntity>
{
    public IReadOnlyList<TEntity> Items { get; set; } = new List<TEntity>();
    public int TotalCount { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
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