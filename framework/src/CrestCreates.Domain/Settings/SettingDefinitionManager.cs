using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public class SettingDefinitionManager : ISettingDefinitionManager
{
    private readonly IReadOnlyList<SettingDefinitionGroup> _groups;
    private readonly IReadOnlyDictionary<string, SettingDefinition> _definitions;

    public SettingDefinitionManager(IEnumerable<ISettingDefinitionProvider> providers)
    {
        var context = new SettingDefinitionContext();

        foreach (var provider in providers)
        {
            provider.Define(context);
        }

        _groups = context.Groups
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _definitions = new ReadOnlyDictionary<string, SettingDefinition>(
            _groups
                .SelectMany(group => group.Definitions)
                .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        if (group.Count() > 1)
                        {
                            throw new InvalidOperationException($"设置 '{group.Key}' 被重复定义");
                        }

                        return group.Single();
                    },
                    StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<SettingDefinitionGroup> GetGroups()
    {
        return _groups;
    }

    public IReadOnlyList<SettingDefinition> GetAll()
    {
        return _definitions.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public SettingDefinition? GetOrNull(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        _definitions.TryGetValue(name.Trim(), out var definition);
        return definition;
    }
}
