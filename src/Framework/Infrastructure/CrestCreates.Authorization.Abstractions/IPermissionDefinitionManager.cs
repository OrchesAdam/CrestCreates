using System.Collections.Generic;

namespace CrestCreates.Authorization.Abstractions;

public interface IPermissionDefinitionManager
{
    PermissionDefinition Get(string name);
    PermissionDefinition? GetOrNull(string name);
    IEnumerable<PermissionDefinition> GetPermissions();
    IEnumerable<PermissionGroupDefinition> GetGroups();
}
