using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Contracts.DTOs.Features;

public class FeatureDefinitionDto
{
    public string Name { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? DefaultValue { get; set; }

    public FeatureValueType ValueType { get; set; }

    public bool IsVisible { get; set; }

    public bool IsEditable { get; set; }

    public FeatureScope Scopes { get; set; }
}
