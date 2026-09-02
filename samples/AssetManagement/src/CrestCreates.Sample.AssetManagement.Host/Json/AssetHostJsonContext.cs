using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.DynamicApi;
using CrestCreates.Sample.AssetManagement.Contracts.Dtos;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.Sample.AssetManagement.Host.Json;

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(DynamicApiResponse<AssetResult>))]
[JsonSerializable(typeof(DynamicApiResponse<AssetOperationResult>))]
[JsonSerializable(typeof(DynamicApiResponse<IReadOnlyList<AssetResult>>))]
[JsonSerializable(typeof(DynamicApiResponse<object>))]
public sealed partial class AssetHostJsonContext : JsonSerializerContext;

public sealed class AssetCombinedJsonResolver : IJsonTypeInfoResolver
{
    private readonly IJsonTypeInfoResolver _contract = AssetJsonContext.Default;
    private readonly IJsonTypeInfoResolver _host = AssetHostJsonContext.Default;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        => _contract.GetTypeInfo(type, options) ?? _host.GetTypeInfo(type, options);
}
