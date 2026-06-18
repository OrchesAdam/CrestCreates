using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public class FeatureValueEntry
{
    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public FeatureScope Scope { get; init; }

    public string ProviderKey { get; init; } = string.Empty;

    public string? TenantId { get; init; }
}
