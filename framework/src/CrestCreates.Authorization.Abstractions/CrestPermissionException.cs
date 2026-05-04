using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Authorization.Abstractions;

public class CrestPermissionException : CrestException
{
    public CrestPermissionException(string permissionName)
        : this(permissionName, $"Permission denied: {permissionName}")
    {
    }

    public CrestPermissionException(string permissionName, string message)
        : base("Crest.Auth.Forbidden", 403, message, permissionName)
    {
        PermissionName = permissionName;
    }

    public CrestPermissionException(string permissionName, string message, Exception innerException)
        : base("Crest.Auth.Forbidden", 403, message, permissionName, innerException)
    {
        PermissionName = permissionName;
    }

    public string PermissionName { get; }
}
