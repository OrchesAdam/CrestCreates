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

/// <summary>
/// A non-contributed host resolver whose nullable-scalar metadata carries a custom converter.
/// Source-generated nullable metadata resolves its underlying converter from the querying options,
/// so a changed nullable wire must be probed through explicit runtime metadata (the same shape as
/// <see cref="ReviewS3HostContext"/> uses for Guid).
/// </summary>
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