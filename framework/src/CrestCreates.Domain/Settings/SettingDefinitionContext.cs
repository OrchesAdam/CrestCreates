using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Shared.Settings;

namespace CrestCreates.Domain.Settings;

public class SettingDefinitionContext
{
    private readonly List<SettingDefinitionGroup> _groups = new();

    public IReadOnlyList<SettingDefinitionGroup> Groups => _groups;

    public SettingDefinitionGroup AddGroup(
        string name,
        string? displayName = null,
        string? description = null)
    {
        if (_groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"设置分组 '{name}' 已存在");
        }

        var group = new SettingDefinitionGroup(name, displayName, description);
        _groups.Add(group);
        return group;
    }

    public SettingDefinitionGroup GetOrAddGroup(
        string name,
        string? displayName = null,
        string? description = null)
    {
        var group = _groups.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        return group ?? AddGroup(name, displayName, description);
    }
}
