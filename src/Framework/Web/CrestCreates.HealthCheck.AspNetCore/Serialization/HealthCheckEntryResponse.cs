using System;
using System.Collections.Generic;

namespace CrestCreates.HealthCheck.AspNetCore.Serialization;

public sealed record HealthCheckEntryResponse
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string? Description { get; init; }
    public string? Exception { get; init; }
    public HealthReportData? Data { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
