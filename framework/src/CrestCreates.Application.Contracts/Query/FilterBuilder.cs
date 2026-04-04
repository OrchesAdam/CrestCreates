using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Contracts.Query;

public class FilterBuilder<T>
{
    private readonly List<FilterDescriptor> _filters = new List<FilterDescriptor>();

    public FilterBuilder<T> Equal<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.Equals, value));
        return this;
    }

    public FilterBuilder<T> NotEqual<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.NotEquals, value));
        return this;
    }

    public FilterBuilder<T> Contains(Expression<Func<T, string>> property, string value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.Contains, value));
        return this;
    }

    public FilterBuilder<T> StartsWith(Expression<Func<T, string>> property, string value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.StartsWith, value));
        return this;
    }

    public FilterBuilder<T> EndsWith(Expression<Func<T, string>> property, string value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.EndsWith, value));
        return this;
    }

    public FilterBuilder<T> GreaterThan<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.GreaterThan, value));
        return this;
    }

    public FilterBuilder<T> GreaterThanOrEqual<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.GreaterThanOrEqual, value));
        return this;
    }

    public FilterBuilder<T> LessThan<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.LessThan, value));
        return this;
    }

    public FilterBuilder<T> LessThanOrEqual<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.LessThanOrEqual, value));
        return this;
    }

    public FilterBuilder<T> In<TProperty>(Expression<Func<T, TProperty>> property, IEnumerable<TProperty> values)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.In, values));
        return this;
    }

    public FilterBuilder<T> NotIn<TProperty>(Expression<Func<T, TProperty>> property, IEnumerable<TProperty> values)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.NotIn, values));
        return this;
    }

    public FilterBuilder<T> IsNull<TProperty>(Expression<Func<T, TProperty>> property)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.IsNull));
        return this;
    }

    public FilterBuilder<T> IsNotNull<TProperty>(Expression<Func<T, TProperty>> property)
    {
        var propertyName = GetPropertyName(property);
        _filters.Add(new FilterDescriptor(propertyName, FilterOperator.IsNotNull));
        return this;
    }

    public List<FilterDescriptor> Build()
    {
        return new List<FilterDescriptor>(_filters);
    }

    public static FilterBuilder<T> Create()
    {
        return new FilterBuilder<T>();
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
