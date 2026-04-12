using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public class ResolvedFeatureValue
{
    public string Name { get; init; } = string.Empty;

    public string? Value { get; init; }

    public FeatureScope? Scope { get; init; }

    public string? ProviderKey { get; init; }

    public string? TenantId { get; init; }
}
