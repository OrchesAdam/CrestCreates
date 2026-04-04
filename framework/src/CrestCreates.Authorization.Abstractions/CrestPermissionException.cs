using System;

namespace CrestCreates.Authorization.Abstractions;

public class CrestPermissionException : Exception
{
    public string PermissionName { get; }

    public CrestPermissionException(string permissionName)
        : base($"Permission '{permissionName}' is not granted.")
    {
        PermissionName = permissionName;
    }

    public CrestPermissionException(string permissionName, string message)
        : base(message)
    {
        PermissionName = permissionName;
    }

    public CrestPermissionException(string permissionName, string message, Exception innerException)
        : base(message, innerException)
    {
        PermissionName = permissionName;
    }
}
