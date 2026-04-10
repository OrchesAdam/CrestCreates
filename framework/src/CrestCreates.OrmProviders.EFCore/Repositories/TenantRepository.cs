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

public class TenantRepository : EfCoreRepositoryBase<Tenant, Guid>, ITenantRepository
{
    public TenantRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<Tenant?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim().ToUpperInvariant();

        return GetQueryable()
            .Include(tenant => tenant.ConnectionStrings)
            .FirstOrDefaultAsync(tenant => tenant.NormalizedName == normalizedName, cancellationToken);
    }

    public Task<Tenant?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Include(tenant => tenant.ConnectionStrings)
            .FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
    }

    public Task<List<Tenant>> GetListWithDetailsAsync(CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Include(tenant => tenant.ConnectionStrings)
            .OrderBy(tenant => tenant.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Tenant>> GetListByLifecycleStateAsync(TenantLifecycleState state, CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Include(tenant => tenant.ConnectionStrings)
            .Where(tenant => tenant.LifecycleState == state)
            .OrderBy(tenant => tenant.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Tenant>> GetArchivedTenantsAsync(CancellationToken cancellationToken = default)
    {
        return GetListByLifecycleStateAsync(TenantLifecycleState.Archived, cancellationToken);
    }
}
