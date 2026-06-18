using System.Collections.Generic;

namespace CrestCreates.Application.Contracts.DTOs.Settings;

public class SettingGroupDto
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public IReadOnlyList<SettingDefinitionDto> Definitions { get; set; } = [];
}
