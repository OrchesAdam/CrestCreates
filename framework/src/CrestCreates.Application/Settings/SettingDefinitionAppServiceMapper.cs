using System.Linq;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Domain.Settings;

namespace CrestCreates.Application.Settings;

public class SettingDefinitionAppServiceMapper
{
    private readonly ISettingEncryptionService _settingEncryptionService;

    public SettingDefinitionAppServiceMapper(ISettingEncryptionService settingEncryptionService)
    {
        _settingEncryptionService = settingEncryptionService;
    }

    public SettingDefinitionDto Map(CrestCreates.Domain.Shared.Settings.SettingDefinition definition)
    {
        return new SettingDefinitionDto
        {
            Name = definition.Name,
            GroupName = definition.GroupName,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            DefaultValue = definition.IsEncrypted ? null : definition.DefaultValue,
            MaskedDefaultValue = definition.IsEncrypted
                ? _settingEncryptionService.Mask(definition.DefaultValue)
                : definition.DefaultValue ?? string.Empty,
            ValueType = definition.ValueType,
            IsEncrypted = definition.IsEncrypted,
            Scopes = definition.Scopes
        };
    }

    public SettingGroupDto Map(CrestCreates.Domain.Shared.Settings.SettingDefinitionGroup group)
    {
        return new SettingGroupDto
        {
            Name = group.Name,
            DisplayName = group.DisplayName,
            Description = group.Description,
            Definitions = group.Definitions.Select(Map).ToArray()
        };
    }
}
