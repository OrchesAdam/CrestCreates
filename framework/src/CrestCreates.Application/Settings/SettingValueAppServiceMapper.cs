using System;
using CrestCreates.Application.Contracts.DTOs.Settings;
using CrestCreates.Domain.Settings;

namespace CrestCreates.Application.Settings;

public class SettingValueAppServiceMapper
{
    private readonly ISettingDefinitionManager _settingDefinitionManager;
    private readonly ISettingEncryptionService _settingEncryptionService;

    public SettingValueAppServiceMapper(
        ISettingDefinitionManager settingDefinitionManager,
        ISettingEncryptionService settingEncryptionService)
    {
        _settingDefinitionManager = settingDefinitionManager;
        _settingEncryptionService = settingEncryptionService;
    }

    public SettingValueDto Map(ResolvedSettingValue value)
    {
        var definition = _settingDefinitionManager.GetOrNull(value.Name)
                         ?? throw new InvalidOperationException($"未定义的设置项: {value.Name}");

        return new SettingValueDto
        {
            Name = definition.Name,
            GroupName = definition.GroupName,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ValueType = definition.ValueType,
            IsEncrypted = definition.IsEncrypted,
            AllowedScopes = definition.Scopes,
            Scope = value.Scope,
            ProviderKey = value.ProviderKey,
            TenantId = value.TenantId,
            HasValue = value.Value is not null,
            Value = definition.IsEncrypted ? null : value.Value,
            MaskedValue = definition.IsEncrypted ? _settingEncryptionService.Mask(value.Value) : value.Value ?? string.Empty
        };
    }

    public SettingValueDto Map(SettingValueEntry? entry, string settingName)
    {
        var definition = _settingDefinitionManager.GetOrNull(settingName)
                         ?? throw new InvalidOperationException($"未定义的设置项: {settingName}");

        return new SettingValueDto
        {
            Name = definition.Name,
            GroupName = definition.GroupName,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            ValueType = definition.ValueType,
            IsEncrypted = definition.IsEncrypted,
            AllowedScopes = definition.Scopes,
            Scope = entry?.Scope,
            ProviderKey = entry?.ProviderKey,
            TenantId = entry?.TenantId,
            HasValue = entry is not null,
            Value = definition.IsEncrypted ? null : entry?.Value,
            MaskedValue = definition.IsEncrypted ? _settingEncryptionService.Mask(entry?.Value) : entry?.Value ?? string.Empty
        };
    }
}
