using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Application.Contracts.DTOs.Settings;

public class SettingDefinitionDto
{
    public string Name { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? DefaultValue { get; set; }

    public string MaskedDefaultValue { get; set; } = string.Empty;

    public SettingValueType ValueType { get; set; }

    public bool IsEncrypted { get; set; }

    public SettingScope Scopes { get; set; }
}
