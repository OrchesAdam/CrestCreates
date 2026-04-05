using System.Collections.Generic;

namespace CrestCreates.Domain.Shared.Permissions;

public interface IEntityPermissions
{
    string EntityName { get; }

    IEnumerable<string> GetAllPermissions();
}
