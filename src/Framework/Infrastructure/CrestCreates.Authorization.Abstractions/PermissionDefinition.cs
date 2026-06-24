using System.Collections.Generic;

namespace CrestCreates.Authorization.Abstractions;

public class PermissionDefinition
{
    public string Name { get; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
    public PermissionDefinition? Parent { get; set; }
    public List<PermissionDefinition> Children { get; }
    public bool IsEnabledByDefault { get; set; }
    public string? GroupName { get; set; }
    public Dictionary<string, object> Properties { get; }

    public PermissionDefinition(
        string name,
        string? displayName = null,
        string? description = null,
        bool isEnabledByDefault = false)
    {
        Name = name ?? throw new System.ArgumentNullException(nameof(name));
        DisplayName = displayName ?? name;
        Description = description;
        IsEnabledByDefault = isEnabledByDefault;
        Children = new List<PermissionDefinition>();
        Properties = new Dictionary<string, object>();
    }

    public PermissionDefinition AddChild(
        string name,
        string? displayName = null,
        string? description = null,
        bool isEnabledByDefault = false)
    {
        var child = new PermissionDefinition(name, displayName, description, isEnabledByDefault)
        {
            Parent = this,
            GroupName = this.GroupName
        };

        Children.Add(child);
        return child;
    }

    public PermissionDefinition WithProperty(string key, object value)
    {
        Properties[key] = value;
        return this;
    }

    public IEnumerable<PermissionDefinition> GetAllDescendants()
    {
        foreach (var child in Children)
        {
            yield return child;

            foreach (var descendant in child.GetAllDescendants())
            {
                yield return descendant;
            }
        }
    }
}
