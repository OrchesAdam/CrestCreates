using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.DynamicApi;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using Microsoft.AspNetCore.Mvc;

namespace CrestCreates.Sample.Procurement.Host.Json;

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(DynamicApiResponse<SubmitProcurementRequestResult>))]
[JsonSerializable(typeof(DynamicApiResponse<ProcurementRequestResult>))]
[JsonSerializable(typeof(DynamicApiResponse<object>))]
public sealed partial class ProcurementHostJsonContext : JsonSerializerContext;

public sealed class ProcurementCombinedJsonResolver : IJsonTypeInfoResolver
{
    private readonly IJsonTypeInfoResolver _contract = ProcurementJsonContext.Default;
    private readonly IJsonTypeInfoResolver _host = ProcurementHostJsonContext.Default;

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        return _contract.GetTypeInfo(type, options) ?? _host.GetTypeInfo(type, options);
    }
}
