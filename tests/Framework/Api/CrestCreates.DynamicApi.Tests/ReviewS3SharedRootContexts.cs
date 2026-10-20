using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.DynamicApi.Tests;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DynamicApiResponse))]
internal partial class ReviewS3OwnedContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DynamicApiResponse))]
internal partial class ReviewS3CanonicalHostContext : JsonSerializerContext;

internal sealed class ReviewS3HostContext(string mode) : JsonSerializerContext(null), IJsonTypeInfoResolver
{
    private readonly Dictionary<(Type, JsonSerializerOptions), JsonTypeInfo> _cache = new();
    protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
    public override JsonTypeInfo? GetTypeInfo(Type type) => Resolve(type, Options);
    JsonTypeInfo? IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options) => Resolve(type, options);
    private JsonTypeInfo? Resolve(Type type, JsonSerializerOptions options)
    {
        if (_cache.TryGetValue((type, options), out var cached)) return cached;
        if (mode == "reflection") return new DefaultJsonTypeInfoResolver().GetTypeInfo(type, options);
        if (type == typeof(Guid))
            return _cache[(type, options)] = JsonMetadataServices.CreateValueInfo<Guid>(options, new ReviewS3GuidConverter());
        var metadata = ((IJsonTypeInfoResolver)ReviewS3OwnedContext.Default).GetTypeInfo(type, options);
        if (type == typeof(DynamicApiResponse) && metadata is not null)
            foreach (var property in metadata.Properties) property.Name = "host_" + property.Name;
        if (metadata is not null) _cache[(type, options)] = metadata;
        return metadata;
    }
}

internal sealed class ReviewS3GuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        => Guid.Parse(reader.GetString()!.Replace("host:", ""));
    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        => writer.WriteStringValue("host:" + value.ToString("N"));
}
