using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Contracts.DTOs.Features;

public class UpdateFeatureValueDto
{
    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }

    public FeatureScope Scope { get; set; }

    public string? TenantId { get; set; }
}
