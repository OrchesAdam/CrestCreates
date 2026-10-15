using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// 过滤描述符
/// </summary>
/// <remarks>
/// #128 S3: the JSON wire contract of this type is owned by <see cref="FilterDescriptorJsonConverter"/>,
/// which bounds the value domain to null/scalar/same-kind scalar arrays staged as a constrained
/// <see cref="System.Text.Json.JsonElement"/> (§8). CLR callers keep the existing scalar and collection
/// surface, bounded by the same rule through the setter.
/// </remarks>
[JsonConverter(typeof(FilterDescriptorJsonConverter))]
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
            if (value != null && !FilterValueDomain.IsStorableValue(value))
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
}
