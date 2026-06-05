using System;
using System.Collections.Generic;

namespace CrestCreates.MultiTenancy.Abstract;

public class TenantInitializationResult
{
    public bool Success { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string? Error { get; init; }
    public IReadOnlyList<TenantInitializationStep> Steps { get; init; } = Array.Empty<TenantInitializationStep>();

    public static TenantInitializationResult Succeeded(string correlationId, IReadOnlyList<TenantInitializationStep> steps)
        => new() { Success = true, CorrelationId = correlationId, Steps = steps };

    public static TenantInitializationResult Failed(string correlationId, string error, IReadOnlyList<TenantInitializationStep> steps)
        => new() { Success = false, CorrelationId = correlationId, Error = error, Steps = steps };
}
