using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using Microsoft.EntityFrameworkCore;

namespace CrestCreates.OrmProviders.EFCore.Repositories;

public class FeatureRepository : EfCoreRepository<FeatureValue, System.Guid>, IFeatureRepository
{
    public FeatureRepository(IDataBaseContext dbContext)
        : base(dbContext)
    {
    }

    public Task<FeatureValue?> FindAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return GetQueryable()
            .FirstOrDefaultAsync(
                feature => feature.Name == name &&
                           feature.Scope == scope &&
                           feature.ProviderKey == providerKey &&
                           feature.TenantId == tenantId,
                cancellationToken);
    }

    public Task<List<FeatureValue>> GetListByScopeAsync(
        FeatureScope scope,
        string? providerKey = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var query = GetQueryable().Where(feature => feature.Scope == scope);

        if (providerKey != null)
        {
            query = query.Where(feature => feature.ProviderKey == providerKey);
        }

        if (tenantId == null)
        {
            query = query.Where(feature => feature.TenantId == null);
        }
        else
        {
            query = query.Where(feature => feature.TenantId == tenantId);
        }

        return query
            .OrderBy(feature => feature.Name)
            .ToListAsync(cancellationToken);
    }
}
