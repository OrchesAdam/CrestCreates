using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrestCreates.HealthCheck.AspNetCore.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthReportResponse))]
[JsonSerializable(typeof(HealthCheckEntryResponse))]
internal partial class HealthReportJsonContext : JsonSerializerContext
{
}

public sealed record HealthReportResponse
{
    public string Status { get; init; } = string.Empty;
    public TimeSpan TotalDuration { get; init; }
    public IReadOnlyList<HealthCheckEntryResponse> Checks { get; init; } = Array.Empty<HealthCheckEntryResponse>();
}
