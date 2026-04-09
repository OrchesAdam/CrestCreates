using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class PermissionGrantRepository : EfCoreRepositoryBase<PermissionGrant, Guid>, IPermissionGrantRepository
{
    public PermissionGrantRepository(
        IDataBaseContext dbContext,
        ICurrentTenant currentTenant,
        DataFilterState dataFilterState)
        : base(dbContext, currentTenant, dataFilterState)
    {
    }

    public Task<List<PermissionGrant>> GetListByProviderAsync(
        PermissionGrantProviderType providerType,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .Where(grant => grant.ProviderType == providerType && grant.ProviderKey == providerKey)
            .OrderBy(grant => grant.PermissionName)
            .ToListAsync(cancellationToken);
    }

    public Task<PermissionGrant?> FindAsync(
        string permissionName,
        PermissionGrantProviderType providerType,
        string providerKey,
        PermissionGrantScope scope,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable().Where(grant =>
            grant.PermissionName == permissionName &&
            grant.ProviderType == providerType &&
            grant.ProviderKey == providerKey &&
            grant.Scope == scope);

        if (scope == PermissionGrantScope.Global)
        {
            query = query.Where(grant => grant.TenantId == null || grant.TenantId == string.Empty);
        }
        else
        {
            query = query.Where(grant => grant.TenantId == tenantId);
        }

        return query.FirstOrDefaultAsync(cancellationToken);
    }
}
