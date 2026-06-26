using CrestCreates.Domain.Shared.Exceptions;

namespace CrestCreates.Domain.Exceptions;

public class CrestPermissionException : CrestException
{
    public CrestPermissionException(string permissionName)
        : base(CrestErrorCodes.AuthForbidden, 403, $"Permission '{permissionName}' was not granted.", permissionName)
    {
        PermissionName = permissionName;
    }

    public string PermissionName { get; }
}
