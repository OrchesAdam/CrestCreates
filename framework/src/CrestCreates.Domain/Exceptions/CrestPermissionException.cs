using System;

namespace CrestCreates.Domain.Exceptions
{
    public class CrestPermissionException : Exception
    {
        public string PermissionName { get; }

        public CrestPermissionException(string permissionName)
            : base($"Permission '{permissionName}' was not granted.")
        {
            PermissionName = permissionName;
        }
    }
}
