using System;
using System.Collections.Generic;
using System.Text.Json;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// #128 S3: the finite value domain of a filter on the JSON wire. §8 allows exactly null, a scalar and
/// an array of same-kind scalars — marshalled as a constrained <see cref="JsonElement"/> so a generated
/// field adapter can read it according to the field's static CLR type without guessing numeric types
/// and without any <c>object</c> converter or reflection fallback. Nested objects, heterogeneous arrays,
/// arrays carrying objects/arrays/null elements and non-bounded elements are rejected here, which makes
/// them a deterministic 400 through the typed body reader.
/// </summary>
internal static class FilterValueDomain
{
    public static bool IsBoundedWireValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => true,
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => true,
        JsonValueKind.Array => IsSameKindScalarArray(element),
        _ => false,
    };

    private static bool IsSameKindScalarArray(JsonElement array)
    {
        JsonValueKind? elementKind = null;
        foreach (var item in array.EnumerateArray())
        {
            JsonValueKind kind;
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    kind = JsonValueKind.String;
                    break;
                case JsonValueKind.Number:
                    kind = JsonValueKind.Number;
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    kind = JsonValueKind.True;
                    break;
                default:
                    // Objects, nested arrays and null elements are outside the finite wire domain.
                    return false;
            }

            if (elementKind is null)
            {
                elementKind = kind;
            }
            else if (elementKind != kind)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsBoundedClrValue(object value) =>
        value is string
        || value.GetType().IsPrimitive
        || value is DateTime
        || value is DateTimeOffset
        || value is TimeSpan
        || value is Guid
        || value is decimal
        || value is double
        || value is float
        || value is IEnumerable<string>
        || value is IEnumerable<Guid>
        || value is IEnumerable<int>
        || value is IEnumerable<long>
        || value is IEnumerable<decimal>
        || value is IEnumerable<double>
        || value is IEnumerable<bool>
        || value is IEnumerable<DateTime>;

    /// <summary>
    /// The single acceptance rule for <see cref="FilterDescriptor.Value"/>: the existing CLR scalar and
    /// collection surface plus the constrained wire staging. Nothing else may be staged on the
    /// descriptor, so a deserializer cannot smuggle an arbitrary graph into the query path.
    /// </summary>
    public static bool IsStorableValue(object value)
    {
        if (value is JsonElement element)
        {
            return IsBoundedWireValue(element);
        }

        return IsBoundedClrValue(value);
    }

    /// <summary>
    /// The scalar-value render used by the converter's write path for CLR values.
    /// </summary>
    public static void WriteClrValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                return;
            case bool flag:
                writer.WriteBooleanValue(flag);
                return;
            case DateTime moment:
                writer.WriteStringValue(moment);
                return;
            case DateTimeOffset offset:
                writer.WriteStringValue(offset);
                return;
            case TimeSpan span:
                writer.WriteStringValue(
                    span.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
                return;
            case Guid id:
                writer.WriteStringValue(id);
                return;
            case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                WriteNumeric(writer, value);
                return;
            default:
                WriteEnumerable(writer, value);
                return;
        }
    }

    private static void WriteNumeric(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case sbyte number:
                writer.WriteNumberValue(number);
                return;
            case byte number:
                writer.WriteNumberValue(number);
                return;
            case short number:
                writer.WriteNumberValue(number);
                return;
            case ushort number:
                writer.WriteNumberValue(number);
                return;
            case int number:
                writer.WriteNumberValue(number);
                return;
            case uint number:
                writer.WriteNumberValue(number);
                return;
            case long number:
                writer.WriteNumberValue(number);
                return;
            case ulong number:
                writer.WriteNumberValue(number);
                return;
            case float number:
                writer.WriteNumberValue(number);
                return;
            case double number:
                writer.WriteNumberValue(number);
                return;
            case decimal number:
                writer.WriteNumberValue(number);
                return;
        }
    }

    private static void WriteEnumerable(Utf8JsonWriter writer, object value)
    {
        writer.WriteStartArray();
        foreach (var item in (System.Collections.IEnumerable)value)
        {
            if (item is null)
            {
                writer.WriteNullValue();
                continue;
            }

            WriteClrValue(writer, item);
        }

        writer.WriteEndArray();
    }
}
