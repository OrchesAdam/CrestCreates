using System;
using System.Text.Json;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Application.Settings;

public class SettingValueTypeConverter
{
    public void Validate(string? value, SettingValueType valueType, string settingName)
    {
        try
        {
            _ = ConvertToValue(value, valueType, typeof(string));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException($"设置 '{settingName}' 的值不符合类型 {valueType}", ex);
        }
    }

    public T? ConvertTo<T>(string? value, SettingValueType valueType)
    {
        var converted = ConvertToValue(value, valueType, typeof(T));
        if (converted is null)
        {
            return default;
        }

        return (T)converted;
    }

    private static object? ConvertToValue(string? value, SettingValueType valueType, Type targetType)
    {
        if (value is null)
        {
            return null;
        }

        return valueType switch
        {
            SettingValueType.String => ConvertString(value, targetType),
            SettingValueType.Int => ConvertInt(value, targetType),
            SettingValueType.Bool => ConvertBool(value, targetType),
            SettingValueType.Json => ConvertJson(value, targetType),
            _ => throw new InvalidOperationException($"不支持的设置值类型: {valueType}")
        };
    }

    private static object ConvertString(string value, Type targetType)
    {
        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return value;
        }

        return JsonSerializer.Deserialize(value, targetType)
               ?? throw new InvalidOperationException("字符串设置无法转换为目标类型");
    }

    private static object ConvertInt(string value, Type targetType)
    {
        if (!int.TryParse(value, out var intValue))
        {
            throw new FormatException("Int 设置值非法");
        }

        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return value;
        }

        if (targetType == typeof(int) || targetType == typeof(int?))
        {
            return intValue;
        }

        return Convert.ChangeType(intValue, Nullable.GetUnderlyingType(targetType) ?? targetType);
    }

    private static object ConvertBool(string value, Type targetType)
    {
        if (!bool.TryParse(value, out var boolValue))
        {
            throw new FormatException("Bool 设置值非法");
        }

        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return value;
        }

        if (targetType == typeof(bool) || targetType == typeof(bool?))
        {
            return boolValue;
        }

        return Convert.ChangeType(boolValue, Nullable.GetUnderlyingType(targetType) ?? targetType);
    }

    private static object ConvertJson(string value, Type targetType)
    {
        using var document = JsonDocument.Parse(value);

        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return value;
        }

        if (targetType == typeof(JsonDocument))
        {
            return JsonDocument.Parse(value);
        }

        return JsonSerializer.Deserialize(value, targetType)
               ?? throw new InvalidOperationException("Json 设置值无法转换为目标类型");
    }
}
