#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CrestCreates.Application.Contracts.Query;

/// <summary>
/// #128 S3 (§8): the static value reader generated field adapters use. A filter value arrives either as
/// the constrained <see cref="JsonElement"/> staging of the finite wire domain or as a CLR scalar from an
/// in-process caller; the reader converts it according to the field's <b>static CLR type</b> — it never
/// guesses a numeric type from the JSON token, never truncates, and never falls back to
/// <c>Convert.ChangeType</c>. Anything that does not fit the field's type is rejected with
/// <see cref="FilterValueFormatException"/> before the query executes.
/// </summary>
public static class FilterValueReader
{
    public static string ReadString(object? value, string field)
    {
        if (value is string text)
        {
            return text;
        }

        if (TryGetElement(value, field, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        throw Mismatch(value, field, "string");
    }

    public static bool ReadBool(object? value, string field)
    {
        if (value is bool flag)
        {
            return flag;
        }

        if (TryGetElement(value, field, out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Mismatch(value, field, "boolean"),
            };
        }

        throw Mismatch(value, field, "boolean");
    }

    public static int ReadInt32(object? value, string field)
    {
        if (value is int number)
        {
            return number;
        }

        if (TryGetElement(value, field, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        throw Mismatch(value, field, "32-bit integer");
    }

    public static long ReadInt64(object? value, string field)
    {
        if (value is long number)
        {
            return number;
        }

        if (TryGetElement(value, field, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out var parsed))
        {
            return parsed;
        }

        throw Mismatch(value, field, "64-bit integer");
    }

    /// <summary>
    /// Decimal fields read the wire text through <c>TryGetDecimal</c>, so a value beyond decimal's
    /// range is rejected instead of being silently degraded to a double.
    /// </summary>
    public static decimal ReadDecimal(object? value, string field)
    {
        if (value is decimal number)
        {
            return number;
        }

        if (TryGetElement(value, field, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDecimal(out var parsed))
        {
            return parsed;
        }

        throw Mismatch(value, field, "decimal");
    }

    public static double ReadDouble(object? value, string field)
    {
        if (value is double number)
        {
            return number;
        }

        if (TryGetElement(value, field, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var parsed))
        {
            return parsed;
        }

        throw Mismatch(value, field, "double");
    }

    public static Guid ReadGuid(object? value, string field)
    {
        if (value is Guid id)
        {
            return id;
        }

        if (TryGetElement(value, field, out var element)
            && element.ValueKind == JsonValueKind.String
            && Guid.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        throw Mismatch(value, field, "GUID");
    }

    public static DateTime ReadDateTime(object? value, string field)
    {
        if (value is DateTime moment)
        {
            return moment;
        }

        if (TryGetElement(value, field, out var element)
            && element.ValueKind == JsonValueKind.String
            && element.TryGetDateTime(out var parsed))
        {
            return parsed;
        }

        throw Mismatch(value, field, "date-time");
    }

    /// <summary>
    /// Enums accept the defined name or the defined numeric value; an undefined name or number is a
    /// rejection, never coerced into the default member.
    /// </summary>
    public static TEnum ReadEnum<TEnum>(object? value, string field)
        where TEnum : struct, Enum
    {
        if (value is TEnum typed)
        {
            return typed;
        }

        if (TryGetElement(value, field, out var element))
        {
            if (element.ValueKind == JsonValueKind.String
                && Enum.TryParse<TEnum>(element.GetString(), ignoreCase: false, out var named)
                && Enum.IsDefined(named))
            {
                return named;
            }

            if (element.ValueKind == JsonValueKind.Number
                && TryReadUnderlyingEnumNumber(element, typeof(TEnum), out var numeric)
                && Enum.IsDefined(typeof(TEnum), numeric!))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), numeric!);
            }
        }

        throw Mismatch(value, field, typeof(TEnum).Name);
    }

    /// <summary>
    /// Reads a wire number into the enum's exact underlying CLR type, so the definition check uses the
    /// same boxed type the enum declares: <c>byte</c>/<c>short</c>/<c>long</c>/<c>ulong</c> enums accept
    /// or reject numeric members by their real range (a boxed Int32 would throw for a byte enum, and
    /// Int32 range checks would wrongly reject legal long members). Nothing is truncated or guessed.
    /// </summary>
    private static bool TryReadUnderlyingEnumNumber(JsonElement element, Type enumType, out object? numeric)
    {
        var underlying = Enum.GetUnderlyingType(enumType);

        if (underlying == typeof(byte))
        {
            if (element.TryGetByte(out var byteValue))
            {
                numeric = byteValue;
                return true;
            }
        }
        else if (underlying == typeof(sbyte))
        {
            if (element.TryGetSByte(out var sbyteValue))
            {
                numeric = sbyteValue;
                return true;
            }
        }
        else if (underlying == typeof(short))
        {
            if (element.TryGetInt16(out var shortValue))
            {
                numeric = shortValue;
                return true;
            }
        }
        else if (underlying == typeof(ushort))
        {
            if (element.TryGetUInt16(out var ushortValue))
            {
                numeric = ushortValue;
                return true;
            }
        }
        else if (underlying == typeof(uint))
        {
            if (element.TryGetUInt32(out var uintValue))
            {
                numeric = uintValue;
                return true;
            }
        }
        else if (underlying == typeof(long))
        {
            if (element.TryGetInt64(out var longValue))
            {
                numeric = longValue;
                return true;
            }
        }
        else if (underlying == typeof(ulong))
        {
            if (element.TryGetUInt64(out var ulongValue))
            {
                numeric = ulongValue;
                return true;
            }
        }
        else if (element.TryGetInt32(out var intValue))
        {
            numeric = intValue;
            return true;
        }

        numeric = null;
        return false;
    }

    /// <summary>
    /// Reads a same-kind scalar array element-wise with the field's scalar reader; empty arrays are
    /// allowed, null elements and non-arrays are rejected (the wire domain already refuses them).
    /// </summary>
    public static IReadOnlyList<T> ReadArray<T>(
        object? value,
        string field,
        Func<object?, string, T> scalarReader)
    {
        var result = new List<T>();
        if (TryGetElement(value, field, out var element))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw Mismatch(value, field, "array");
            }

            foreach (var item in element.EnumerateArray())
            {
                result.Add(scalarReader(item, field));
            }

            return result;
        }

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    throw Mismatch(value, field, "array of non-null scalars");
                }

                result.Add(scalarReader(item, field));
            }

            return result;
        }

        throw Mismatch(value, field, "array");
    }

    private static bool TryGetElement(object? value, string field, out JsonElement element)
    {
        if (value is JsonElement staged)
        {
            element = staged;
            return true;
        }

        element = default;
        return false;
    }

    private static FilterValueFormatException Mismatch(object? value, string field, string expected) =>
        new($"Filter value for field '{field}' must be a {expected}; observed '{Describe(value)}'.");

    private static string Describe(object? value) => value switch
    {
        null => "null",
        JsonElement element => element.ValueKind.ToString(),
        _ => value.GetType().Name,
    };
}

/// <summary>
/// A filter value that does not fit its field's static type. §8 requires such values to be rejected
/// before the query executes instead of being coerced or ignored.
/// </summary>
public sealed class FilterValueFormatException : Exception
{
    public FilterValueFormatException(string message)
        : base(message)
    {
    }
}
