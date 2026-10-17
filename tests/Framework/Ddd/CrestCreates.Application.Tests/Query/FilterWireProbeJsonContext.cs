#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CrestCreates.Application.Contracts.DTOs.Common;

namespace CrestCreates.Application.Tests.Query;

internal sealed class FilterWireProbeRequest
{
    public List<FilterDescriptor> Filters { get; set; } = new();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FilterWireProbeRequest))]
internal partial class FilterWireProbeJsonContext : JsonSerializerContext
{
}
