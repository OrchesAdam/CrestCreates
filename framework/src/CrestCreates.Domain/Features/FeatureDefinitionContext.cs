using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public class FeatureDefinitionContext
{
    private readonly List<FeatureDefinitionGroup> _groups = new();

    public IReadOnlyList<FeatureDefinitionGroup> Groups => _groups;

    public FeatureDefinitionGroup AddGroup(
        string name,
        string? displayName = null,
        string? description = null)
    {
        if (_groups.Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"功能特性分组 '{name}' 已存在");
        }

        var group = new FeatureDefinitionGroup(name, displayName, description);
        _groups.Add(group);
        return group;
    }

    public FeatureDefinitionGroup GetOrAddGroup(
        string name,
        string? displayName = null,
        string? description = null)
    {
        var group = _groups.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        return group ?? AddGroup(name, displayName, description);
    }
}
