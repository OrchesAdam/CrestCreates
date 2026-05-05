using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

public class FeatureAuditEntry
{
    public string FeatureName { get; init; } = string.Empty;
    public FeatureScope Scope { get; init; }
    public string? TenantId { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string Operation { get; init; } = string.Empty;
}
