using System;

namespace CrestCreates.Domain.Shared.ObjectMapping;

/// <summary>
/// 指定属性映射时使用的自定义值转换器。
/// 转换器必须是静态类，包含 static TResult Convert(TSource value) 方法。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class MapConvertAttribute : Attribute
{
    public Type ConverterType { get; }

    public MapConvertAttribute(Type converterType)
    {
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }
}
