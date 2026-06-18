using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories;

namespace CrestCreates.Domain.Repositories.Permission
{
    public interface ITenantDomainMappingRepository : ICrestRepositoryBase<TenantDomainMapping, Guid>
    {
        Task<TenantDomainMapping?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default);
        Task<TenantDomainMapping?> FindBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
        Task<List<TenantDomainMapping>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    }
}
