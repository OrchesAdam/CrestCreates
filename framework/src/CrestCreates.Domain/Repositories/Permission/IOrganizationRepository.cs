using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface IOrganizationRepository : ICrestRepositoryBase<Organization, Guid>
    {
        Task<List<Organization>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
        Task<List<Organization>> GetAllChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);
        Task<List<Organization>> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default);
    }
}
