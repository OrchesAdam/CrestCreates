using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestCreates.Domain.Shared.Features;

public class FeatureDefinitionGroup
{
    private readonly List<FeatureDefinition> _definitions = new();

    public FeatureDefinitionGroup(
        string name,
        string? displayName = null,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("功能特性分组名称不能为空", nameof(name));
        }

        Name = name.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Name : displayName.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public string Name { get; }

    public string DisplayName { get; }

    public string? Description { get; }

    public IReadOnlyList<FeatureDefinition> Definitions => _definitions;

    public FeatureDefinition AddDefinition(
        string name,
        string? displayName = null,
        string? description = null,
        string? defaultValue = null,
        FeatureValueType valueType = FeatureValueType.Bool,
        bool isVisible = true,
        bool isEditable = true,
        FeatureScope scopes = FeatureScope.Global)
    {
        if (_definitions.Any(definition => string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"功能特性 '{name}' 已存在于分组 '{Name}' 中");
        }

        var definition = new FeatureDefinition(
            name,
            Name,
            displayName,
            description,
            defaultValue,
            valueType,
            isVisible,
            isEditable,
            scopes);

        _definitions.Add(definition);
        return definition;
    }
}
