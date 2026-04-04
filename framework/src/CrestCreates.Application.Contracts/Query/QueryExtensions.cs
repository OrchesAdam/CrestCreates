using System.Collections.Generic;
using System.Linq;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Contracts.Query;

public static class QueryExtensions
{
    public static IQueryable<T> ApplyFilters<T>(this IQueryable<T> query, List<FilterDescriptor> filters)
    {
        return QueryExecutor<T>.ApplyFilters(query, filters);
    }

    public static IQueryable<T> ApplySorts<T>(this IQueryable<T> query, List<SortDescriptor> sorts)
    {
        return QueryExecutor<T>.ApplySorts(query, sorts);
    }

    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, int skip, int take)
    {
        return QueryExecutor<T>.ApplyPaging(query, skip, take);
    }

    public static IQueryable<T> ApplyQueryRequest<T>(this IQueryable<T> query, QueryRequest<T> request)
    {
        return QueryExecutor<T>.Execute(query, request);
    }

    public static FilterBuilder<T> ToFilterBuilder<T>(this List<FilterDescriptor> filters)
    {
        var builder = FilterBuilder<T>.Create();
        foreach (var filter in filters)
        {
        }
        return builder;
    }

    public static SortBuilder<T> ToSortBuilder<T>(this List<SortDescriptor> sorts)
    {
        var builder = SortBuilder<T>.Create();
        foreach (var sort in sorts)
        {
        }
        return builder;
    }
}
