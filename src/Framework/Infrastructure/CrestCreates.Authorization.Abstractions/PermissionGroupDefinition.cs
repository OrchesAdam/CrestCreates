using System.Collections.Generic;

namespace CrestCreates.Authorization.Abstractions;

public class PermissionGroupDefinition
{
    public string Name { get; }
    public string DisplayName { get; set; }
    public List<PermissionDefinition> Permissions { get; }

    public PermissionGroupDefinition(string name, string? displayName = null)
    {
        Name = name ?? throw new System.ArgumentNullException(nameof(name));
        DisplayName = displayName ?? name;
        Permissions = new List<PermissionDefinition>();
    }

    public PermissionDefinition AddPermission(
        string name,
        string? displayName = null,
        string? description = null,
        bool isEnabledByDefault = false)
    {
        var permission = new PermissionDefinition(name, displayName, description, isEnabledByDefault)
        {
            GroupName = this.Name
        };

        Permissions.Add(permission);
        return permission;
    }

    public IEnumerable<PermissionDefinition> GetAllPermissions()
    {
        foreach (var permission in Permissions)
        {
            yield return permission;

            foreach (var child in permission.GetAllDescendants())
            {
                yield return child;
            }
        }
    }
}
