using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using CrestCreates.Domain.Shared.DTOs;
using CrestCreates.Domain.Shared.Enums;

namespace CrestCreates.Domain.Shared.Query;

public static class QueryExecutor<T>
{
    public static IQueryable<T> ApplyFilters(IQueryable<T> query, List<FilterDescriptor> filters)
    {
        if (filters == null || !filters.Any())
        {
            return query;
        }

        foreach (var filter in filters)
        {
            query = ApplyFilter(query, filter);
        }

        return query;
    }

    public static IQueryable<T> ApplySorts(IQueryable<T> query, List<SortDescriptor> sorts)
    {
        if (sorts == null || !sorts.Any())
        {
            return query;
        }

        IOrderedQueryable<T>? orderedQuery = null;

        for (int i = 0; i < sorts.Count; i++)
        {
            var sort = sorts[i];
            var parameter = Expression.Parameter(typeof(T), "x");
            var property = Expression.Property(parameter, sort.Field);
            var lambda = Expression.Lambda(property, parameter);

            string methodName;
            if (i == 0)
            {
                methodName = sort.Direction == SortDirection.Ascending ? "OrderBy" : "OrderByDescending";
            }
            else
            {
                methodName = sort.Direction == SortDirection.Ascending ? "ThenBy" : "ThenByDescending";
            }

            var methodCall = Expression.Call(
                typeof(Queryable),
                methodName,
                new Type[] { typeof(T), property.Type },
                (i == 0 ? query : orderedQuery!).Expression,
                Expression.Quote(lambda)
            );

            orderedQuery = query.Provider.CreateQuery<T>(methodCall) as IOrderedQueryable<T>;
        }

        return orderedQuery ?? query;
    }

    public static IQueryable<T> ApplyPaging(IQueryable<T> query, int skip, int take)
    {
        return query.Skip(skip).Take(take);
    }

    public static IQueryable<T> Execute(IQueryable<T> query, QueryRequest<T> request)
    {
        query = ApplyFilters(query, request.Filters ?? new List<FilterDescriptor>());
        query = ApplySorts(query, request.Sorts ?? new List<SortDescriptor>());
        query = ApplyPaging(query, request.GetSkipCount(), request.PageSize);
        return query;
    }

    private static IQueryable<T> ApplyFilter(IQueryable<T> query, FilterDescriptor filter)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, filter.Field);
        Expression? body = null;

        switch (filter.Operator)
        {
            case FilterOperator.Equals:
                body = CreateEqualityExpression(property, filter.Value);
                break;
            case FilterOperator.NotEquals:
                body = Expression.Not(CreateEqualityExpression(property, filter.Value));
                break;
            case FilterOperator.Contains:
                body = CreateContainsExpression(property, filter.Value);
                break;
            case FilterOperator.StartsWith:
                body = CreateStartsWithExpression(property, filter.Value);
                break;
            case FilterOperator.EndsWith:
                body = CreateEndsWithExpression(property, filter.Value);
                break;
            case FilterOperator.GreaterThan:
                body = Expression.GreaterThan(property, Expression.Constant(filter.Value));
                break;
            case FilterOperator.GreaterThanOrEqual:
                body = Expression.GreaterThanOrEqual(property, Expression.Constant(filter.Value));
                break;
            case FilterOperator.LessThan:
                body = Expression.LessThan(property, Expression.Constant(filter.Value));
                break;
            case FilterOperator.LessThanOrEqual:
                body = Expression.LessThanOrEqual(property, Expression.Constant(filter.Value));
                break;
            case FilterOperator.In:
                body = CreateInExpression(property, filter.Value);
                break;
            case FilterOperator.NotIn:
                body = Expression.Not(CreateInExpression(property, filter.Value));
                break;
            case FilterOperator.IsNull:
                body = Expression.Equal(property, Expression.Constant(null, property.Type));
                break;
            case FilterOperator.IsNotNull:
                body = Expression.NotEqual(property, Expression.Constant(null, property.Type));
                break;
        }

        if (body != null)
        {
            var lambda = Expression.Lambda<Func<T, bool>>(body, parameter);
            query = query.Where(lambda);
        }

        return query;
    }

    private static BinaryExpression CreateEqualityExpression(MemberExpression property, object? value)
    {
        var valueConstant = Expression.Constant(value);
        return Expression.Equal(property, valueConstant);
    }

    private static MethodCallExpression CreateContainsExpression(MemberExpression property, object? value)
    {
        var method = typeof(string).GetMethod("Contains", new[] { typeof(string) });
        var valueConstant = Expression.Constant(value, typeof(string));
        return Expression.Call(property, method!, valueConstant);
    }

    private static MethodCallExpression CreateStartsWithExpression(MemberExpression property, object? value)
    {
        var method = typeof(string).GetMethod("StartsWith", new[] { typeof(string) });
        var valueConstant = Expression.Constant(value, typeof(string));
        return Expression.Call(property, method!, valueConstant);
    }

    private static MethodCallExpression CreateEndsWithExpression(MemberExpression property, object? value)
    {
        var method = typeof(string).GetMethod("EndsWith", new[] { typeof(string) });
        var valueConstant = Expression.Constant(value, typeof(string));
        return Expression.Call(property, method!, valueConstant);
    }

    private static MethodCallExpression CreateInExpression(MemberExpression property, object? values)
    {
        var method = typeof(Enumerable).GetMethods()
            .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
            .MakeGenericMethod(property.Type);
        
        var valuesConstant = Expression.Constant(values);
        return Expression.Call(null, method, valuesConstant, property);
    }
}
