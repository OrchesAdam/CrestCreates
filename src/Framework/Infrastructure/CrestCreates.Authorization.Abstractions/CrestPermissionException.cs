using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Authorization.Abstractions;

public class CrestPermissionException : CrestException
{
    public CrestPermissionException(string permissionName)
        : this(permissionName, $"Permission denied: {permissionName}")
    {
    }

    public CrestPermissionException(string permissionName, string message)
        : base(CrestErrorCodes.AuthForbidden, 403, message, permissionName)
    {
        PermissionName = permissionName;
    }

    public CrestPermissionException(string permissionName, string message, Exception innerException)
        : base(CrestErrorCodes.AuthForbidden, 403, message, permissionName, innerException)
    {
        PermissionName = permissionName;
    }

    public string PermissionName { get; }
}
