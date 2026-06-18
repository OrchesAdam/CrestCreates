using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.Authorization.Abstractions;

namespace CrestCreates.Authorization;

public class PermissionDefinitionManager : IPermissionDefinitionManager
{
    private readonly Lazy<PermissionDefinitionContext> _context;

    public PermissionDefinitionManager(IEnumerable<IPermissionDefinitionProvider> providers)
    {
        _context = new Lazy<PermissionDefinitionContext>(() =>
        {
            var context = new PermissionDefinitionContext();

            foreach (var provider in providers)
            {
                provider.Define(context);
            }

            return context;
        });
    }

    public PermissionDefinition Get(string name)
    {
        var permission = GetOrNull(name);
        if (permission == null)
        {
            throw new InvalidOperationException($"Permission '{name}' not found.");
        }

        return permission;
    }

    public PermissionDefinition? GetOrNull(string name)
    {
        return _context.Value.GetPermissionOrNull(name);
    }

    public IEnumerable<PermissionDefinition> GetPermissions()
    {
        return _context.Value.GetPermissions();
    }

    public IEnumerable<PermissionGroupDefinition> GetGroups()
    {
        return _context.Value.GetGroups();
    }
}

internal class PermissionDefinitionContext : IPermissionDefinitionContext
{
    private readonly Dictionary<string, PermissionGroupDefinition> _groups;

    public PermissionDefinitionContext()
    {
        _groups = new Dictionary<string, PermissionGroupDefinition>();
    }

    public PermissionGroupDefinition AddGroup(string name, string? displayName = null)
    {
        if (_groups.ContainsKey(name))
        {
            throw new InvalidOperationException($"Permission group '{name}' already exists.");
        }

        var group = new PermissionGroupDefinition(name, displayName);
        _groups[name] = group;
        return group;
    }

    public PermissionGroupDefinition? GetGroupOrNull(string name)
    {
        return _groups.TryGetValue(name, out var group) ? group : null;
    }

    public void RemoveGroup(string name)
    {
        _groups.Remove(name);
    }

    public IEnumerable<PermissionGroupDefinition> GetGroups()
    {
        return _groups.Values;
    }

    public IEnumerable<PermissionDefinition> GetPermissions()
    {
        foreach (var group in _groups.Values)
        {
            foreach (var permission in group.GetAllPermissions())
            {
                yield return permission;
            }
        }
    }

    public PermissionDefinition? GetPermissionOrNull(string name)
    {
        return GetPermissions().FirstOrDefault(p => p.Name == name);
    }
}
