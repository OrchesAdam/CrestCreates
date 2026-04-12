using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Domain.Features;

public class FeatureDefinitionManager : IFeatureDefinitionManager
{
    private readonly IReadOnlyList<FeatureDefinitionGroup> _groups;
    private readonly IReadOnlyDictionary<string, FeatureDefinition> _definitions;

    public FeatureDefinitionManager(IEnumerable<IFeatureDefinitionProvider> providers)
    {
        var context = new FeatureDefinitionContext();

        foreach (var provider in providers)
        {
            provider.Define(context);
        }

        _groups = context.Groups
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _definitions = new ReadOnlyDictionary<string, FeatureDefinition>(
            _groups
                .SelectMany(group => group.Definitions)
                .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        if (group.Count() > 1)
                        {
                            throw new InvalidOperationException($"功能特性 '{group.Key}' 被重复定义");
                        }

                        return group.Single();
                    },
                    StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyList<FeatureDefinitionGroup> GetGroups()
    {
        return _groups;
    }

    public IReadOnlyList<FeatureDefinition> GetAll()
    {
        return _definitions.Values
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public FeatureDefinition? GetOrNull(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        _definitions.TryGetValue(name.Trim(), out var definition);
        return definition;
    }
}
