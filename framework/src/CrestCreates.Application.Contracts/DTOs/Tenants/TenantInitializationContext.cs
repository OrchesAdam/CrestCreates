using System;

namespace CrestCreates.Application.Contracts.DTOs.Tenants;

public class TenantInitializationContext
{
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string? ConnectionString { get; init; }
    public bool IsIndependentDatabase => !string.IsNullOrWhiteSpace(ConnectionString);
    public string CorrelationId { get; init; } = string.Empty;
    public Guid? RequestedByUserId { get; init; }
}
