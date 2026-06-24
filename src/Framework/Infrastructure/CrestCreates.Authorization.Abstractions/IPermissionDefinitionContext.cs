namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionDefinitionContext
{
    PermissionGroupDefinition AddGroup(string name, string? displayName = null);
    PermissionGroupDefinition? GetGroupOrNull(string name);
    void RemoveGroup(string name);
}
