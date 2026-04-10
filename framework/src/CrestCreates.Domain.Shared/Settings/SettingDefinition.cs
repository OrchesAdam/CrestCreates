using System;

namespace CrestCreates.Domain.Shared.Settings;

public class SettingDefinition
{
    public SettingDefinition(
        string name,
        string groupName,
        string? displayName = null,
        string? description = null,
        string? defaultValue = null,
        SettingValueType valueType = SettingValueType.String,
        bool isEncrypted = false,
        SettingScope scopes = SettingScope.Global)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("设置名称不能为空", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(groupName))
        {
            throw new ArgumentException("设置分组不能为空", nameof(groupName));
        }

        Name = name.Trim();
        GroupName = groupName.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultValue = defaultValue;
        ValueType = valueType;
        IsEncrypted = isEncrypted;
        Scopes = scopes;
    }

    public string Name { get; }

    public string GroupName { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public string? DefaultValue { get; }

    public SettingValueType ValueType { get; }

    public bool IsEncrypted { get; }

    public SettingScope Scopes { get; }

    public bool SupportsScope(SettingScope scope)
    {
        return (Scopes & scope) == scope;
    }
}
