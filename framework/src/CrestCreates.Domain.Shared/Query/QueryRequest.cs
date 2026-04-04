using System.Collections.Generic;
using CrestCreates.Domain.Shared.DTOs;

namespace CrestCreates.Domain.Shared.Query;

public class QueryRequest<T> : PagedRequestDto
{
    public QueryRequest()
    {
    }

    public QueryRequest(List<FilterDescriptor> filters, List<SortDescriptor> sorts)
    {
        Filters = filters;
        Sorts = sorts;
    }

    public QueryRequest(int pageIndex, int pageSize)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
    }

    public QueryRequest(int pageIndex, int pageSize, List<FilterDescriptor> filters, List<SortDescriptor> sorts)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        Filters = filters;
        Sorts = sorts;
    }

    public static QueryRequest<T> Create()
    {
        return new QueryRequest<T>();
    }

    public static QueryRequest<T> CreateWithFilters(List<FilterDescriptor> filters)
    {
        return new QueryRequest<T> { Filters = filters };
    }

    public static QueryRequest<T> CreateWithSorts(List<SortDescriptor> sorts)
    {
        return new QueryRequest<T> { Sorts = sorts };
    }

    public static QueryRequest<T> CreatePaged(int pageIndex, int pageSize)
    {
        return new QueryRequest<T>(pageIndex, pageSize);
    }
}
