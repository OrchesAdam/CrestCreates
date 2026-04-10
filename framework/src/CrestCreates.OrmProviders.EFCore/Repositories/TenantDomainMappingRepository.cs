using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class TenantDomainMappingRepository : EfCoreRepositoryBase<TenantDomainMapping, Guid>, ITenantDomainMappingRepository
{
    public TenantDomainMappingRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public async Task<TenantDomainMapping?> FindByDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        var normalizedDomain = domain.ToLowerInvariant();
        return await GetQueryable()
            .FirstOrDefaultAsync(m => m.Domain == normalizedDomain && m.IsActive, cancellationToken);
    }

    public async Task<TenantDomainMapping?> FindBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default)
    {
        var normalizedSubdomain = subdomain.ToLowerInvariant();
        return await GetQueryable()
            .FirstOrDefaultAsync(m => m.Subdomain == normalizedSubdomain && m.IsActive, cancellationToken);
    }

    public Task<List<TenantDomainMapping>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(m => m.TenantId == tenantId)
            .OrderByDescending(m => m.Priority)
            .ThenBy(m => m.Domain)
            .ToListAsync(cancellationToken);
    }
}
