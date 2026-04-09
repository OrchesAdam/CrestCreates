using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission;

public interface IUserRoleRepository : ICrestRepositoryBase<UserRole, Guid>
{
    Task<UserRole?> FindAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
    Task<List<UserRole>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<List<UserRole>> GetByRoleIdAsync(Guid roleId, CancellationToken cancellationToken = default);
}
