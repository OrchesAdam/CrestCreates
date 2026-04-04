using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Contracts.Query;

public class SortBuilder<T>
{
    private readonly List<SortDescriptor> _sorts = new List<SortDescriptor>();

    public SortBuilder<T> Asc<TProperty>(Expression<Func<T, TProperty>> property)
    {
        var propertyName = GetPropertyName(property);
        _sorts.Add(new SortDescriptor(propertyName, SortDirection.Ascending));
        return this;
    }

    public SortBuilder<T> Desc<TProperty>(Expression<Func<T, TProperty>> property)
    {
        var propertyName = GetPropertyName(property);
        _sorts.Add(new SortDescriptor(propertyName, SortDirection.Descending));
        return this;
    }

    public List<SortDescriptor> Build()
    {
        return new List<SortDescriptor>(_sorts);
    }

    public static SortBuilder<T> Create()
    {
        return new SortBuilder<T>();
    }

    private static string GetPropertyName<TProperty>(Expression<Func<T, TProperty>> property)
    {
        if (property.Body is MemberExpression memberExpression)
        {
            return memberExpression.Member.Name;
        }
        throw new ArgumentException("Expression must be a member access expression", nameof(property));
    }
}
