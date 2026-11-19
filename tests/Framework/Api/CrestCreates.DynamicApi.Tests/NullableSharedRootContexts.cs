using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.DynamicApi.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(int?))]
internal partial class NullableScalarOwnedContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(int?))]
internal partial class NullableScalarForeignContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(int?))]
internal partial class NullableScalarCanonicalHostContext : JsonSerializerContext;

internal sealed class NullableUnderlyingIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => int.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>A host resolver that replaces the nullable wrapper converter itself.</summary>
internal sealed class NullableScalarModifiedHostContext : JsonSerializerContext, IJsonTypeInfoResolver
{
    public NullableScalarModifiedHostContext() : base(null)
    {
    }

    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;

    public override JsonTypeInfo? GetTypeInfo(Type type) => ((IJsonTypeInfoResolver)this).GetTypeInfo(type, Options);

    JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        => type == typeof(int?)
            ? JsonMetadataServices.CreateValueInfo<int?>(options, new NullableIntHostConverter())
            : null;
}

internal sealed class NullableIntHostConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : int.Parse(reader.GetString()!.Replace("host:", ""));

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue("host:" + value.Value);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CrudWireProbeDto))]
internal partial class PrivateRootForeignContext : JsonSerializerContext;