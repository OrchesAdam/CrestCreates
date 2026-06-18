using System;
using System.Collections.Generic;
using CrestCreates.Domain.Shared;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantInitializationResult
{
    public bool Success { get; init; }
    public TenantInitializationStatus Status { get; init; }
    public string? Error { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public IReadOnlyList<TenantInitializationStep> Steps { get; init; } = Array.Empty<TenantInitializationStep>();

    public static TenantInitializationResult Succeeded(
        string correlationId,
        IReadOnlyList<TenantInitializationStep> steps)
        => new()
        {
            Success = true,
            Status = TenantInitializationStatus.Initialized,
            CorrelationId = correlationId,
            Steps = steps
        };

    public static TenantInitializationResult Failed(
        string correlationId,
        string error,
        IReadOnlyList<TenantInitializationStep> steps)
        => new()
        {
            Success = false,
            Status = TenantInitializationStatus.Failed,
            Error = error,
            CorrelationId = correlationId,
            Steps = steps
        };
}

public class TenantInitializationStep
{
    public string Name { get; init; } = string.Empty;
    public TenantInitializationStepStatus Status { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Error { get; init; }
}
