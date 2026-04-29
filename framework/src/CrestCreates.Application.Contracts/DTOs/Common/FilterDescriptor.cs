using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// 过滤描述符
/// </summary>
public class FilterDescriptor
{
    /// <summary>
    /// 过滤字段名称
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// 过滤操作符
    /// </summary>
    public FilterOperator Operator { get; set; } = FilterOperator.Equals;

    private object? _value;
    /// <summary>
    /// 过滤值
    /// </summary>
    public object? Value 
    {
        get => _value;
        set
        {
            if (value != null && !IsValidValueType(value))
            {
                throw new System.ArgumentException($"不支持的过滤值类型: {value.GetType()}");
            }
            _value = value;
        }
    }

    /// <summary>
    /// 创建过滤描述符实例
    /// </summary>
    public FilterDescriptor()
    {
    }

    /// <summary>
    /// 创建过滤描述符实例
    /// </summary>
    /// <param name="field">过滤字段名称</param>
    /// <param name="operator">过滤操作符</param>
    /// <param name="value">过滤值</param>
    public FilterDescriptor(string field, FilterOperator @operator, object? value = null)
    {
        Field = field;
        Operator = @operator;
        Value = value;
    }

    private bool IsValidValueType(object value)
    {
        return value is string
            || value.GetType().IsPrimitive
            || value is System.DateTime
            || value is System.DateTimeOffset
            || value is System.TimeSpan
            || value is System.Guid
            || value is IEnumerable<string>
            || value is IEnumerable<System.Guid>
            || value is IEnumerable<int>
            || value is IEnumerable<long>;
    }
}
