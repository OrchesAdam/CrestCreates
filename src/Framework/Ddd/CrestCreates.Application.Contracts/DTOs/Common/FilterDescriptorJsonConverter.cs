using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.Application.Contracts.DTOs.Common;

/// <summary>
/// #128 S3: the dedicated wire converter for <see cref="FilterDescriptor"/>. It exists so the JSON wire
/// can carry the bounded filter value domain of §8 — null, a scalar and a same-kind scalar array staged
/// as a constrained <see cref="JsonElement"/> — without introducing a global <c>JsonConverter&lt;object&gt;</c>
/// or any converter fallback. Everything outside that domain (nested objects, arrays of objects or nulls,
/// heterogeneous arrays, undefined operators) is rejected as a <see cref="JsonException"/>, which the
/// typed body reader surfaces as a 400 before any business code runs. CLR callers keep the existing
/// scalar/collection surface, so the converter is a wire concern and not a new CLR contract.
/// </summary>
public sealed class FilterDescriptorJsonConverter : JsonConverter<FilterDescriptor>
{
    public override FilterDescriptor Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A filter must be a JSON object.");
        }

        var descriptor = new FilterDescriptor();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return descriptor;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Malformed filter member.");
            }

            var member = reader.GetString();
            reader.Read();

            if (string.Equals(member, nameof(FilterDescriptor.Field), StringComparison.OrdinalIgnoreCase))
            {
                descriptor.Field = reader.TokenType == JsonTokenType.String
                    ? reader.GetString() ?? string.Empty
                    : throw new JsonException("The filter field must be a string.");
                continue;
            }

            if (string.Equals(member, nameof(FilterDescriptor.Operator), StringComparison.OrdinalIgnoreCase))
            {
                descriptor.Operator = ReadOperator(ref reader);
                continue;
            }

            if (string.Equals(member, nameof(FilterDescriptor.Value), StringComparison.OrdinalIgnoreCase))
            {
                descriptor.Value = ReadBoundedValue(ref reader);
                continue;
            }

            // Unknown members are ignored so a newer payload cannot break an older host.
            reader.Skip();
        }

        throw new JsonException("Unexpected end of filter object.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        FilterDescriptor value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("field", value.Field);
        writer.WriteNumber("operator", (int)value.Operator);
        writer.WritePropertyName("value");

        switch (value.Value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            default:
                FilterValueDomain.WriteClrValue(writer, value.Value);
                break;
        }

        writer.WriteEndObject();
    }

    private static FilterOperator ReadOperator(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
            {
                var numeric = reader.GetInt32();
                if (!Enum.IsDefined(typeof(FilterOperator), numeric))
                {
                    throw new JsonException($"Unsupported filter operator '{numeric}'.");
                }

                return (FilterOperator)numeric;
            }

            case JsonTokenType.String:
            {
                var name = reader.GetString();
                if (name is null || !Enum.TryParse<FilterOperator>(name, ignoreCase: true, out var parsed)
                    || !Enum.IsDefined(typeof(FilterOperator), parsed))
                {
                    throw new JsonException($"Unsupported filter operator '{name}'.");
                }

                return parsed;
            }

            default:
                throw new JsonException("The filter operator must be a number or a name.");
        }
    }

    private static JsonElement ReadBoundedValue(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (!FilterValueDomain.IsBoundedWireValue(element))
        {
            throw new JsonException(
                "The filter value must be null, a scalar or a same-kind scalar array; nested objects, "
                + "arrays of objects and heterogeneous arrays are not part of the finite filter domain.");
        }

        // Clone detaches the element from the pooled document so the descriptor stays valid after the
        // reader moves on; the setter re-validates and accepts exactly this bounded staging.
        return element.Clone();
    }
}
