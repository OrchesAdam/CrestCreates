using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Domain.Shared.Settings;

public class SettingDefinitionGroup
{
    private readonly List<SettingDefinition> _definitions = new();

    public SettingDefinitionGroup(
        string name,
        string? displayName = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("设置分组名称不能为空", nameof(name));
        }

        Name = name.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public IReadOnlyList<SettingDefinition> Definitions => _definitions;

    public SettingDefinition AddDefinition(
        string name,
        string? displayName = null,
        string? description = null,
        string? defaultValue = null,
        SettingValueType valueType = SettingValueType.String,
        bool isEncrypted = false,
        SettingScope scopes = SettingScope.Global)
    {
        if (_definitions.Any(definition => string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"设置 '{name}' 已存在于分组 '{Name}' 中");
        }

        var definition = new SettingDefinition(
            name,
            Name,
            displayName,
            description,
            defaultValue,
            valueType,
            isEncrypted,
            scopes);

        _definitions.Add(definition);
        return definition;
    }
}
