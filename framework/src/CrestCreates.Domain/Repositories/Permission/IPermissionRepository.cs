using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Repositories;
using PermissionEntity = CrestCreates.Domain.Permission.Permission;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface IPermissionRepository : ICrestRepositoryBase<PermissionEntity, Guid>
    {
        Task<List<PermissionEntity>> GetByRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default);
        Task<List<PermissionEntity>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<PermissionEntity?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
    }
}
