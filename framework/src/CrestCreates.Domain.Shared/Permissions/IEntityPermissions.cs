using System.Collections.Generic;

namespace CrestCreates.Domain.Shared.Permissions;

public interface IEntityPermissions
{
    string ModuleName => string.Empty;

    string EntityName { get; }

    string PermissionGroupName => string.IsNullOrWhiteSpace(ModuleName)
        ? EntityName
        : $"{ModuleName}.{EntityName}";

    string GetPermissionName(string action)
    {
        return $"{PermissionGroupName}.{action}";
    }

    IEnumerable<string> GetAllPermissions();
}
