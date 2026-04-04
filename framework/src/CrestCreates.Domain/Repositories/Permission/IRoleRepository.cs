using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface IRoleRepository : ICrestRepositoryBase<Role, Guid>
    {
        Task<List<Role>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Role?> FindByNameAsync(string name, string tenantId, CancellationToken cancellationToken = default);
    }
}
