using System;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

public class FeatureValueTypeConverter
{
    public string? ConvertToString<T>(T? value, FeatureValueType valueType)
    {
        if (value == null)
        {
            return null;
        }

        return valueType switch
        {
            FeatureValueType.Bool when typeof(T) == typeof(bool) => value!.ToString()?.ToLowerInvariant(),
            FeatureValueType.Bool when typeof(T) == typeof(string) => NormalizeBoolValue(value as string),
            FeatureValueType.Int when typeof(T) == typeof(int) => value!.ToString(),
            FeatureValueType.Int when typeof(T) == typeof(string) => NormalizeIntValue(value as string),
            FeatureValueType.String => value!.ToString(),
            _ => value!.ToString()
        };
    }

    public T? ConvertTo<T>(string? value, FeatureValueType valueType)
    {
        if (value == null)
        {
            return default;
        }

        var type = typeof(T);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            type = Nullable.GetUnderlyingType(type)!;
        }

        return type switch
        {
            _ when type == typeof(bool) => (T)(object)ParseBool(value),
            _ when type == typeof(int) => (T)(object)ParseInt(value),
            _ when type == typeof(string) => (T)(object)value,
            _ => throw new NotSupportedException($"不支持的类型转换: {type.Name}")
        };
    }

    public void Validate(string? value, FeatureValueType valueType, string featureName)
    {
        if (value == null)
        {
            return;
        }

        switch (valueType)
        {
            case FeatureValueType.Bool:
                if (!IsValidBoolValue(value))
                {
                    throw new ArgumentException($"功能特性 '{featureName}' 需要布尔值，但提供的值 '{value}' 无效");
                }
                break;

            case FeatureValueType.Int:
                if (!IsValidIntValue(value))
                {
                    throw new ArgumentException($"功能特性 '{featureName}' 需要整数值，但提供的值 '{value}' 无效");
                }
                break;

            case FeatureValueType.String:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "不支持的功能特性值类型");
        }
    }

    private static bool IsValidBoolValue(string value)
    {
        return bool.TryParse(value, out _) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidIntValue(string value)
    {
        return int.TryParse(value, out _);
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new ArgumentException($"无法将值 '{value}' 转换为布尔值");
    }

    private static int ParseInt(string value)
    {
        if (int.TryParse(value, out var intValue))
        {
            return intValue;
        }

        throw new ArgumentException($"无法将值 '{value}' 转换为整数值");
    }

    private static string? NormalizeBoolValue(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue.ToString().ToLowerInvariant();
        }

        if (value.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return "true";
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase))
        {
            return "false";
        }

        return null;
    }

    private static string? NormalizeIntValue(string? value)
    {
        if (value == null)
        {
            return null;
        }

        if (int.TryParse(value, out _))
        {
            return value.Trim();
        }

        return null;
    }
}
