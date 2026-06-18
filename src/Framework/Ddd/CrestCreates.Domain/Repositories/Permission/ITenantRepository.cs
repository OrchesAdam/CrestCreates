using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface ITenantRepository : ICrestRepositoryBase<Tenant, Guid>
    {
        Task<Tenant?> FindByNameAsync(string name, CancellationToken cancellationToken = default);
        Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<List<Tenant>> GetListWithDetailsAsync(CancellationToken cancellationToken = default);
        Task<List<Tenant>> GetListByLifecycleStateAsync(TenantLifecycleState state, CancellationToken cancellationToken = default);
        Task<List<Tenant>> GetArchivedTenantsAsync(CancellationToken cancellationToken = default);
    }
}
